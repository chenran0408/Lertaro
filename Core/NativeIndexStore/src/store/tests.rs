use super::*;

#[test]
fn record_size_is_compact_and_alignment_is_stable() {
    assert_eq!(std::mem::size_of::<Record>(), 48);
    assert_eq!(std::mem::align_of::<Record>(), 8);
    assert_eq!(std::mem::size_of::<NameSpan>(), 12);
    assert_eq!(std::mem::size_of::<RecordInput>(), 72);
    assert_eq!(std::mem::size_of::<RecordOutput>(), 64);
    assert_eq!(std::mem::size_of::<HighPair>(), 16);
}

#[test]
fn interning_keeps_unicode_and_duplicate_names() {
    let mut store = RecordStore::new();
    let name = "中文😀".encode_utf16().collect::<Vec<_>>();
    let first = store.intern_name(&name);
    let second = store.intern_name(&name);
    assert_eq!(first, second);
    let span = store.names[first as usize];
    let start = span.offset as usize;
    let end = start + span.length as usize;
    assert_eq!(&store.name_arena[start..end], name);
}

#[test]
fn interning_remains_correct_after_name_table_rebuilds() {
    let mut store = RecordStore::new();
    let names = (0..128)
        .map(|index| {
            format!("directory-{index}")
                .encode_utf16()
                .collect::<Vec<_>>()
        })
        .collect::<Vec<_>>();

    for name in &names {
        store.intern_name(name);
    }

    assert_eq!(store.intern_name(&names[73]), 73);
    assert_eq!(store.names.len(), names.len());
    assert_eq!(store.name_hashes.len(), names.len());
}

#[test]
fn active_sort_matches_uint128_and_stable_tie_order() {
    let mut store = RecordStore::new();
    let mut row = |id_low, id_high, flags| {
        let input = RecordInput {
            id_low,
            id_high,
            flags,
            ..RecordInput::default()
        };
        let name_id = store.intern_name(&[]);
        let record = store.make_record(input, name_id);
        store.records.push(record);
    };
    row(8, 1, 0);
    row(1, 0, 0);
    row(8, 1, 0);
    row(0, 0, 2);
    drop(row);
    store.sort_active_indices();
    assert_eq!(store.sorted_indices, [1, 0, 2]);
}

#[test]
fn large_active_sort_keeps_uint128_order_and_ties() {
    let mut store = RecordStore::new();
    for index in 0..512_u64 {
        let input = RecordInput {
            id_low: index.wrapping_mul(37) % 251,
            id_high: index % 5,
            flags: if index % 11 == 0 { 2 } else { 0 },
            ..RecordInput::default()
        };
        let name_id = store.intern_name(&[]);
        let record = store.make_record(input, name_id);
        store.records.push(record);
    }

    let mut expected = store
        .records
        .iter()
        .enumerate()
        .filter(|(_, record)| record.flags & 2 == 0)
        .map(|(index, record)| {
            (
                high_id_for(&store.high_pairs, record.high_pair_index),
                record.id_low,
                index,
            )
        })
        .collect::<Vec<_>>();
    expected.sort_unstable();

    store.sort_active_indices();
    let actual = store
        .sorted_indices
        .iter()
        .map(|&index| {
            let record = store.records[index as usize];
            (
                high_id_for(&store.high_pairs, record.high_pair_index),
                record.id_low,
                index as usize,
            )
        })
        .collect::<Vec<_>>();

    assert_eq!(actual, expected);
}

#[test]
fn sparse_high_pairs_round_trip_full_uint128_ids() {
    let mut store = RecordStore::new();
    let zero_high_record = store.make_record(RecordInput::default(), 0);
    assert_eq!(zero_high_record.high_pair_index, u32::MAX);
    assert!(store.high_pairs.is_empty());
    let input = RecordInput {
        id_low: 17,
        id_high: 0x1122334455667788,
        parent_low: 29,
        parent_high: 0x99aabbccddeeff00,
        ..RecordInput::default()
    };
    let name_id = store.intern_name(&[]);
    let record = store.make_record(input, name_id);
    store.records.push(zero_high_record);
    store.records.push(record);
    assert_eq!(store.high_pairs.len(), 1);
    let output = store.output(1).unwrap();
    assert_eq!(output.id_low, input.id_low);
    assert_eq!(output.id_high, input.id_high);
    assert_eq!(output.parent_low, input.parent_low);
    assert_eq!(output.parent_high, input.parent_high);
}

#[test]
fn cloning_preserves_interned_storage_without_sorted_views() {
    let mut source = RecordStore::new();
    let name = "中文😀.txt".encode_utf16().collect::<Vec<_>>();
    let name_id = source.intern_name(&name);
    let record = source.make_record(
        RecordInput {
            id_low: 17,
            id_high: 2,
            parent_low: 29,
            parent_high: 3,
            flags: 0,
            ..RecordInput::default()
        },
        name_id,
    );
    source.records.push(record);
    source.sort_active_indices();

    let mut destination = RecordStore::new();
    destination.intern_name(&[9, 9]);
    destination.clone_from(&source);

    assert_eq!(destination.records.len(), source.records.len());
    assert_eq!(destination.records[0].id_low, source.records[0].id_low);
    assert_eq!(
        destination.records[0].high_pair_index,
        source.records[0].high_pair_index
    );
    assert_eq!(destination.name_arena, source.name_arena);
    assert_eq!(destination.names.len(), source.names.len());
    assert_eq!(destination.name_hashes, source.name_hashes);
    assert_eq!(destination.name_slots, source.name_slots);
    assert_eq!(destination.high_pairs.len(), source.high_pairs.len());
    assert_eq!(
        destination.high_pairs[0].id_high,
        source.high_pairs[0].id_high
    );
    assert_eq!(
        destination.high_pairs[0].parent_high,
        source.high_pairs[0].parent_high
    );
    assert!(destination.sorted_indices.is_empty());
    let span = destination.names[name_id as usize];
    let start = span.offset as usize;
    let end = start + span.length as usize;
    assert_eq!(&destination.name_arena[start..end], name);
}
