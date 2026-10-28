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
