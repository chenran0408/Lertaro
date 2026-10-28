use super::*;
use std::slice;
#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_set(
    handle: *mut Handle,
    index: i32,
    input: *const RecordInput,
    name: *const u16,
) -> i32 {
    result_code(|| {
        if input.is_null() {
            return 1;
        }
        let input = unsafe { *input };
        if input.name_length < 0 || (input.name_length > 0 && name.is_null()) {
            return 1;
        }
        let text = if input.name_length == 0 {
            &[]
        } else {
            unsafe { slice::from_raw_parts(name, input.name_length as usize) }
        };
        with_store(handle, |store| {
            let Ok(index) = usize::try_from(index) else {
                return 1;
            };
            if index >= store.records.len() {
                return 1;
            }
            let name_id = store.intern_name(text);
            let record = store.make_record(input, name_id);
            store.records[index] = record;
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_insert(
    handle: *mut Handle,
    index: i32,
    input: *const RecordInput,
    name: *const u16,
) -> i32 {
    result_code(|| {
        if input.is_null() {
            return 1;
        }
        let input = unsafe { *input };
        if input.name_length < 0 || (input.name_length > 0 && name.is_null()) {
            return 1;
        }
        let text = if input.name_length == 0 {
            &[]
        } else {
            unsafe { slice::from_raw_parts(name, input.name_length as usize) }
        };
        with_store(handle, |store| {
            let Ok(index) = usize::try_from(index) else {
                return 1;
            };
            if index > store.records.len() {
                return 1;
            }
            let name_id = store.intern_name(text);
            let record = store.make_record(input, name_id);
            store.records.insert(index, record);
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_remove_at(handle: *mut Handle, index: i32) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            let Ok(index) = usize::try_from(index) else {
                return 1;
            };
            if index >= store.records.len() {
                return 1;
            }
            store.records.remove(index);
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_update_metadata(
    handle: *mut Handle,
    index: i32,
    flags: u16,
    creation_time: u32,
    last_write_time: u32,
    last_access_time: u32,
) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            let Ok(index) = usize::try_from(index) else {
                return 1;
            };
            let Some(record) = store.records.get_mut(index) else {
                return 1;
            };
            record.flags = flags;
            record.creation_time = creation_time;
            record.last_write_time = last_write_time;
            record.last_access_time = last_access_time;
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_extend(
    handle: *mut Handle,
    source: *mut Handle,
    start: i32,
    count: i32,
) -> i32 {
    result_code(|| {
        if handle.is_null() || source.is_null() || start < 0 || count < 0 {
            return 1;
        }
        if handle == source {
            return with_store(handle, |store| {
                let start = start as usize;
                let end = start.saturating_add(count as usize);
                if end > store.records.len() {
                    return 1;
                }
                let copy = store.records[start..end].to_vec();
                store.records.extend_from_slice(&copy);
                store.sorted_indices.clear();
                0
            })
            .unwrap_or(1);
        }
        let src_ref = unsafe { &*source };
        let dst_ref = unsafe { &*handle };
        let (src_guard, mut dst_guard) = if (source as usize) < (handle as usize) {
            (
                src_ref.0.lock().unwrap_or_else(|p| p.into_inner()),
                dst_ref.0.lock().unwrap_or_else(|p| p.into_inner()),
            )
        } else {
            let dst_guard = dst_ref.0.lock().unwrap_or_else(|p| p.into_inner());
            let src_guard = src_ref.0.lock().unwrap_or_else(|p| p.into_inner());
            (src_guard, dst_guard)
        };
        let begin = start as usize;
        let end = begin.saturating_add(count as usize);
        if end > src_guard.records.len() {
            return 1;
        }
        dst_guard.records.reserve(count as usize);
        for row in &src_guard.records[begin..end] {
            let span = src_guard.names[row.name_id as usize];
            let name_start = span.offset as usize;
            let name_end = name_start + span.length as usize;
            let name = &src_guard.name_arena[name_start..name_end];
            let name_id = dst_guard.intern_name(name);
            let input = RecordInput {
                id_low: row.id_low,
                id_high: src_guard.high_pair(row.high_pair_index).id_high,
                parent_low: row.parent_low,
                parent_high: src_guard.high_pair(row.high_pair_index).parent_high,
                flags: row.flags,
                size: row.size,
                creation_time: row.creation_time,
                last_write_time: row.last_write_time,
                last_access_time: row.last_access_time,
                ..RecordInput::default()
            };
            let record = dst_guard.make_record(input, name_id);
            dst_guard.records.push(record);
        }
        dst_guard.sorted_indices.clear();
        0
    })
}
