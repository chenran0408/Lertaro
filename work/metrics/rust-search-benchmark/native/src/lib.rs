#![deny(unsafe_op_in_unsafe_fn)]

#[repr(C)]
pub struct BatchMatch {
    pub uid: i32,
    pub start: i32,
    pub end: i32,
    pub score: i32,
}

const SCORE_MATCH: i16 = 16;
const SCORE_GAP_START: i16 = -3;
const SCORE_GAP_EXTENSION: i16 = -1;
const BONUS_BOUNDARY: i16 = 8;
const BONUS_NON_WORD: i16 = 8;
const BONUS_CAMEL_123: i16 = 7;
const BONUS_CONSECUTIVE: i16 = 4;
const BONUS_FIRST_CHAR_MULTIPLIER: i16 = 2;
const BONUS_BOUNDARY_WHITE: i16 = 10;
const BONUS_BOUNDARY_DELIMITER: i16 = 9;
const MAX_V2_CELLS: usize = 250_000;

#[derive(Clone, Copy, PartialEq, Eq)]
enum CharClass { White, NonWord, Delimiter, Lower, Upper, Letter, Number }

#[inline]
fn class(c: u8) -> CharClass {
    match c {
        b'a'..=b'z' => CharClass::Lower,
        b'A'..=b'Z' => CharClass::Upper,
        b'0'..=b'9' => CharClass::Number,
        b' ' | b'\t' | b'\r' | b'\n' => CharClass::White,
        b'/' | b'\\' | b':' | b';' | b',' | b'|' => CharClass::Delimiter,
        0..=127 => CharClass::NonWord,
        _ => CharClass::Letter,
    }
}

#[inline]
fn bonus(previous: CharClass, current: CharClass) -> i16 {
    if current != CharClass::White {
        if previous == CharClass::White { return BONUS_BOUNDARY_WHITE; }
        if previous == CharClass::Delimiter { return BONUS_BOUNDARY_DELIMITER; }
        if previous == CharClass::NonWord { return BONUS_BOUNDARY; }
    }
    if (previous == CharClass::Lower && current == CharClass::Upper)
        || (previous != CharClass::Number && current == CharClass::Number)
    {
        return BONUS_CAMEL_123;
    }
    match current {
        CharClass::NonWord | CharClass::Delimiter => BONUS_NON_WORD,
        CharClass::White => BONUS_BOUNDARY_WHITE,
        _ => 0,
    }
}

#[inline]
fn lower(c: u8) -> u8 {
    if c.is_ascii_uppercase() { c + 32 } else { c }
}

fn fuzzy_match(
    text: &[u8],
    pattern: &[u8],
    chars: &mut Vec<u8>,
    bonuses: &mut Vec<i16>,
    first: &mut Vec<i32>,
    scores: &mut Vec<i16>,
    consecutive: &mut Vec<i16>,
) -> Option<(i32, i32, i32)> {
    let m = pattern.len();
    if m == 0 { return Some((0, 0, 0)); }
    if m > text.len() { return None; }

    let mut start = 0usize;
    let mut current = 0usize;
    for (pidx, &target) in pattern.iter().enumerate() {
        let target = lower(target);
        let offset = text[current..].iter().position(|&c| lower(c) == target)?;
        let absolute = current + offset;
        if pidx == 0 { start = absolute.saturating_sub(1); }
        current = absolute + 1;
    }
    let mut end = current;
    let last = lower(*pattern.last().unwrap());
    if let Some(offset) = text[end..].iter().rposition(|&c| lower(c) == last) {
        end += offset + 1;
    }

    let scoped = end - start;
    if m > 1000 || scoped.saturating_mul(m) > MAX_V2_CELLS {
        return fuzzy_match_v1(text, pattern, start, end);
    }

    chars.resize(scoped, 0);
    bonuses.resize(scoped, 0);
    first.resize(m, -1);
    for value in first.iter_mut() { *value = -1; }

    let mut pattern_index = 0usize;
    let mut last_idx = 0usize;
    let first_pattern_char = lower(pattern[0]);
    let mut previous_class = CharClass::White;
    for offset in 0..scoped {
        let raw = text[start + offset];
        let current_class = class(raw);
        chars[offset] = lower(raw);
        bonuses[offset] = bonus(previous_class, current_class);
        previous_class = current_class;
        if pattern_index < m && chars[offset] == lower(pattern[pattern_index]) {
            first[pattern_index] = offset as i32;
            last_idx = offset;
            pattern_index += 1;
        }
    }
    if pattern_index != m { return None; }

    if m == 1 {
        let mut best_score = 0i32;
        let mut best_pos = None;
        for i in 0..scoped {
            if chars[i] != first_pattern_char { continue; }
            let score = i32::from(SCORE_MATCH + bonuses[i] * BONUS_FIRST_CHAR_MULTIPLIER);
            if score > best_score {
                best_score = score;
                best_pos = Some(i);
                if bonuses[i] >= BONUS_BOUNDARY { break; }
            }
        }
        return best_pos.map(|pos| ((start + pos) as i32, (start + pos + 1) as i32, best_score));
    }

    let f0 = first[0] as usize;
    let width = last_idx - f0 + 1;
    let matrix_len = m * width;
    scores.resize(matrix_len, 0);
    consecutive.resize(matrix_len, 0);

    let mut in_gap = false;
    let mut previous = 0i16;
    for col in f0..=last_idx {
        let rel = col - f0;
        if chars[col] == first_pattern_char {
            let score = SCORE_MATCH + bonuses[col] * BONUS_FIRST_CHAR_MULTIPLIER;
            scores[rel] = score;
            consecutive[rel] = 1;
            previous = score;
            in_gap = false;
        } else {
            let score = (previous + if in_gap { SCORE_GAP_EXTENSION } else { SCORE_GAP_START }).max(0);
            scores[rel] = score;
            consecutive[rel] = 0;
            previous = score;
            in_gap = true;
        }
    }

    let mut max_score = 0i16;
    let mut max_score_pos = f0;
    for pidx in 1..m {
        let row = pidx * width;
        let previous_row = row - width;
        in_gap = false;
        let start_col = first[pidx] as usize;
        let start_rel = start_col - f0;
        if start_rel > 0 {
            scores[row + start_rel - 1] = 0;
            consecutive[row + start_rel - 1] = 0;
        }
        for col in start_col..=last_idx {
            let rel = col - f0;
            let s2 = if rel > 0 {
                scores[row + rel - 1] + if in_gap { SCORE_GAP_EXTENSION } else { SCORE_GAP_START }
            } else { 0 };
            let mut s1 = 0i16;
            let mut consecutive_score = 0i16;
            if chars[col] == lower(pattern[pidx]) && rel > 0 {
                s1 = scores[previous_row + rel - 1] + SCORE_MATCH;
                let mut b = bonuses[col];
                consecutive_score = consecutive[previous_row + rel - 1] + 1;
                if consecutive_score > 1 {
                    let first_bonus = bonuses[col - consecutive_score as usize + 1];
                    if b >= BONUS_BOUNDARY && b > first_bonus {
                        consecutive_score = 1;
                    } else {
                        b = b.max(first_bonus).max(BONUS_CONSECUTIVE);
                    }
                }
                if s1 + b < s2 {
                    s1 += bonuses[col];
                    consecutive_score = 0;
                } else {
                    s1 += b;
                }
            }
            consecutive[row + rel] = consecutive_score;
            in_gap = s1 < s2;
            let cell_score = s1.max(s2).max(0);
            scores[row + rel] = cell_score;
            if pidx == m - 1 && cell_score > max_score {
                max_score = cell_score;
                max_score_pos = col;
            }
        }
    }

    let mut i = m - 1;
    let mut j = max_score_pos;
    let mut prefer_match = true;
    while j >= first[i] as usize {
        let row = i * width;
        let rel = j - f0;
        let score = scores[row + rel];
        let diagonal = if i > 0 && rel > 0 { scores[row - width + rel - 1] } else { 0 };
        let left = if rel > 0 { scores[row + rel - 1] } else { 0 };
        if score > diagonal && (score > left || (score == left && prefer_match)) {
            if i == 0 { break; }
            i -= 1;
        }
        prefer_match = consecutive[row + rel] > 1
            || (i < m - 1 && rel + 1 < width && rel + 1 >= first[i + 1] as usize - 1
                && consecutive[row + width + rel + 1] > 0);
        if j == 0 { break; }
        j -= 1;
    }
    Some(((start + j) as i32, (start + max_score_pos + 1) as i32, i32::from(max_score)))
}

fn fuzzy_match_v1(text: &[u8], pattern: &[u8], mut start: usize, end: usize) -> Option<(i32, i32, i32)> {
    let mut pidx = pattern.len();
    for i in (start..end).rev() {
        if lower(text[i]) == lower(pattern[pidx - 1]) {
            pidx -= 1;
            if pidx == 0 { start = i; break; }
        }
    }
    if pidx != 0 { return None; }
    let mut score = 0i32;
    let mut in_gap = false;
    let mut consecutive = 0i16;
    let mut first_bonus = 0i16;
    let mut pattern_index = 0usize;
    let mut previous_class = if start > 0 { class(text[start - 1]) } else { CharClass::White };
    for &raw in &text[start..end] {
        let current_class = class(raw);
        let matched = pattern_index < pattern.len() && lower(raw) == lower(pattern[pattern_index]);
        if matched {
            let mut b = bonus(previous_class, current_class);
            score += i32::from(SCORE_MATCH);
            if consecutive == 0 { first_bonus = b; }
            else {
                if b >= BONUS_BOUNDARY && b > first_bonus { first_bonus = b; }
                b = b.max(first_bonus).max(BONUS_CONSECUTIVE);
            }
            score += i32::from(if pattern_index == 0 { b * BONUS_FIRST_CHAR_MULTIPLIER } else { b });
            pattern_index += 1;
            consecutive += 1;
            in_gap = false;
        } else {
            score += i32::from(if in_gap { SCORE_GAP_EXTENSION } else { SCORE_GAP_START });
            in_gap = true;
            consecutive = 0;
            first_bonus = 0;
        }
        previous_class = current_class;
    }
    (pattern_index == pattern.len()).then_some((start as i32, end as i32, score))
}

#[unsafe(no_mangle)]
pub unsafe extern "system" fn lertaro_fuzzy_batch(
    names: *const u8,
    offsets: *const u32,
    name_count: u32,
    query: *const u8,
    query_len: u32,
    output: *mut BatchMatch,
    output_capacity: u32,
) -> u32 {
    if (name_count > 0 && (names.is_null() || offsets.is_null())) || query.is_null() || output.is_null() {
        return 0;
    }
    let names = unsafe { std::slice::from_raw_parts(names, offsets.add(name_count as usize).read() as usize) };
    let offsets = unsafe { std::slice::from_raw_parts(offsets, name_count as usize + 1) };
    let query = unsafe { std::slice::from_raw_parts(query, query_len as usize) };
    let output = unsafe { std::slice::from_raw_parts_mut(output, output_capacity as usize) };
    let mut chars = Vec::new();
    let mut bonuses = Vec::new();
    let mut first = Vec::new();
    let mut scores = Vec::new();
    let mut consecutive = Vec::new();
    let mut written = 0usize;
    for uid in 0..name_count as usize {
        let start = offsets[uid] as usize;
        let end = offsets[uid + 1] as usize;
        if start > end || end > names.len() { continue; }
        if let Some((match_start, match_end, score)) = fuzzy_match(
            &names[start..end], query, &mut chars, &mut bonuses, &mut first, &mut scores, &mut consecutive,
        ) {
            if written < output.len() {
                output[written] = BatchMatch { uid: uid as i32, start: match_start, end: match_end, score };
                written += 1;
            }
        }
    }
    written as u32
}
