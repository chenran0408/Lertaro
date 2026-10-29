use super::*;
use std::panic::catch_unwind;
#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_new() -> *mut Handle {
    catch_unwind(|| Box::into_raw(Box::new(Handle(Mutex::new(RecordStore::new())))))
        .unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_free(handle: *mut Handle) {
    if !handle.is_null() {
        unsafe {
            drop(Box::from_raw(handle));
        }
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_count(handle: *mut Handle) -> i32 {
    with_store(handle, |store| {
        store.records.len().min(i32::MAX as usize) as i32
    })
    .unwrap_or(-1)
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_capacity(handle: *mut Handle) -> i32 {
    with_store(handle, |store| {
        store.records.capacity().min(i32::MAX as usize) as i32
    })
    .unwrap_or(-1)
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_reserve(handle: *mut Handle, capacity: i32) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            if capacity < 0 {
                return 1;
            }
            store
                .records
                .reserve((capacity as usize).saturating_sub(store.records.len()));
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_set_capacity(
    handle: *mut Handle,
    capacity: i32,
) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            if capacity < 0 || (capacity as usize) < store.records.len() {
                return 1;
            }
            store
                .records
                .reserve_exact((capacity as usize).saturating_sub(store.records.len()));
            store.records.shrink_to(capacity as usize);
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_clear(handle: *mut Handle) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            store.records.clear();
            store.name_arena.clear();
            store.names.clear();
            store.name_hashes.clear();
            store.name_slots.clear();
            store.high_pairs.clear();
            store.sorted_indices.clear();
            0
        })
        .unwrap_or(1)
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_record_store_trim(handle: *mut Handle) -> i32 {
    result_code(|| {
        with_store(handle, |store| {
            store.records.shrink_to_fit();
            store.name_arena.shrink_to_fit();
            store.names.shrink_to_fit();
            store.name_hashes.shrink_to_fit();
            store.name_slots.shrink_to_fit();
            store.high_pairs.shrink_to_fit();
            0
        })
        .unwrap_or(1)
    })
}
