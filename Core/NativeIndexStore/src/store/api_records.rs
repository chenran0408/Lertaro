use super::*;
use std::slice;
#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_add(
    handle: *mut Handle,
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
            let name_id = store.intern_name(text);
            let record = store.make_record(input, name_id);
            store.records.push(record);
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_add_batch(
    handle: *mut Handle,
    inputs: *const RecordInput,
    input_count: i32,
    names: *const u16,
    name_count: i32,
) -> i32 {
    result_code(|| {
        if input_count < 0
            || name_count < 0
            || (input_count > 0 && inputs.is_null())
            || (name_count > 0 && names.is_null())
        {
            return 1;
        }
        let inputs = if input_count == 0 {
            &[]
        } else {
            unsafe { slice::from_raw_parts(inputs, input_count as usize) }
        };
        let names = if name_count == 0 {
            &[]
        } else {
            unsafe { slice::from_raw_parts(names, name_count as usize) }
        };
        if inputs.iter().any(|input| {
            input.name_offset < 0
                || input.name_length < 0
                || input.name_offset as usize + input.name_length as usize > names.len()
        }) {
            return 1;
        }
        with_store(handle, |store| {
            store.records.reserve(inputs.len());
            for input in inputs {
                let start = input.name_offset as usize;
                let end = start + input.name_length as usize;
                let name_id = store.intern_name(&names[start..end]);
                let record = store.make_record(*input, name_id);
                store.records.push(record);
            }
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_get(
    handle: *mut Handle,
    index: i32,
    output: *mut RecordOutput,
    name: *mut *const u16,
    name_length: *mut i32,
) -> i32 {
    result_code(|| {
        if output.is_null() || name.is_null() || name_length.is_null() {
            return 1;
        }
        with_store(handle, |store| {
            match usize::try_from(index).ok().and_then(|index| {
                let record = store.output(index)?;
                let (name_ptr, length) = store.name_ptr(record.name_id)?;
                Some((record, name_ptr, length))
            }) {
                Some((record, name_ptr, length)) => {
                    unsafe {
                        *output = record;
                        *name = name_ptr;
                        *name_length = length;
                    }
                    0
                }
                None => 1,
            }
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_view(
    handle: *mut Handle,
    output: *mut *const Record,
    count: *mut i32,
    high_pairs: *mut *const HighPair,
    high_pair_count: *mut i32,
) -> i32 {
    result_code(|| {
        if output.is_null() || count.is_null() || high_pairs.is_null() || high_pair_count.is_null()
        {
            return 1;
        }
        with_store(handle, |store| {
            unsafe {
                *output = store.records.as_ptr();
                *count = store.records.len().min(i32::MAX as usize) as i32;
                *high_pairs = store.high_pairs.as_ptr();
                *high_pair_count = store.high_pairs.len().min(i32::MAX as usize) as i32;
            }
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_sort_active(
    handle: *mut Handle,
    output: *mut *const i32,
    count: *mut i32,
) -> i32 {
    result_code(|| {
        if output.is_null() || count.is_null() {
            return 1;
        }
        with_store(handle, |store| {
            store.sort_active_indices();
            unsafe {
                *output = store.sorted_indices.as_ptr();
                *count = store.sorted_indices.len().min(i32::MAX as usize) as i32;
            }
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_release_sorted(handle: *mut Handle) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            store.sorted_indices = Vec::new();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_get_name(
    handle: *mut Handle,
    name_id: u32,
    output: *mut *const u16,
    length: *mut i32,
) -> i32 {
    result_code(|| {
        if output.is_null() || length.is_null() {
            return 1;
        }
        with_store(handle, |store| match store.name_ptr(name_id) {
            Some((name, name_length)) => {
                unsafe {
                    *output = name;
                    *length = name_length;
                }
                0
            }
            None => 1,
        })
        .unwrap_or(1)
    })
}
