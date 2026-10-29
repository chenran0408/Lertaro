use super::snapshot::{CompactionPatch, CompactionRecord, SnapshotBuilder, SnapshotView};
use super::*;

fn slice_from_raw<'a, T>(pointer: *const T, count: usize) -> &'a [T] {
    if count == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(pointer, count) }
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_snapshot_builder_new(
    handle: *mut Handle,
    root_low: u64,
    root_high: u64,
    output: *mut *mut SnapshotBuilder,
) -> i32 {
    result_code(|| {
        if output.is_null() {
            return 1;
        }
        let Some(builder) = with_store(handle, |store| {
            SnapshotBuilder::build(store, [root_low, root_high])
        }) else {
            return 1;
        };
        unsafe {
            *output = Box::into_raw(Box::new(builder));
        }
        0
    })
}

#[allow(clippy::too_many_arguments)]
#[unsafe(no_mangle)]
pub extern "system" fn lertaro_compaction_snapshot_new(
    base_ids: *const [u64; 2],
    base_name_uids: *const u32,
    base_flags: *const u16,
    base_sizes: *const i64,
    base_creation_times: *const u32,
    base_last_write_times: *const u32,
    base_last_access_times: *const u32,
    base_parent_indexes: *const i32,
    orphan_rows: *const i32,
    orphan_frns: *const [u64; 2],
    actions: *const i32,
    patches: *const CompactionPatch,
    patch_count: i32,
    added: *const CompactionRecord,
    added_count: i32,
    base_count: i32,
    orphan_count: i32,
    unique_count: i32,
    root_low: u64,
    root_high: u64,
    output: *mut *mut SnapshotBuilder,
) -> i32 {
    result_code(|| {
        if output.is_null()
            || base_count < 0
            || orphan_count < 0
            || patch_count < 0
            || added_count < 0
            || unique_count < 0
        {
            return 1;
        }
        let base_count = base_count as usize;
        let orphan_count = orphan_count as usize;
        let patch_count = patch_count as usize;
        let added_count = added_count as usize;
        let unique_count = unique_count as usize;
        let ids = slice_from_raw(base_ids, base_count);
        let name_uids = slice_from_raw(base_name_uids, base_count);
        let flags = slice_from_raw(base_flags, base_count);
        let sizes = slice_from_raw(base_sizes, base_count);
        let creation_times = slice_from_raw(base_creation_times, base_count);
        let last_write_times = slice_from_raw(base_last_write_times, base_count);
        let last_access_times = slice_from_raw(base_last_access_times, base_count);
        let parent_indexes = slice_from_raw(base_parent_indexes, base_count);
        let orphan_rows = slice_from_raw(orphan_rows, orphan_count);
        let orphan_frns = slice_from_raw(orphan_frns, orphan_count);
        let actions = slice_from_raw(actions, base_count);
        let patches = slice_from_raw(patches, patch_count);
        let added = slice_from_raw(added, added_count);
        if ids.len() != name_uids.len()
            || ids.len() != flags.len()
            || ids.len() != sizes.len()
            || ids.len() != creation_times.len()
            || ids.len() != last_write_times.len()
            || ids.len() != last_access_times.len()
            || ids.len() != parent_indexes.len()
            || ids.len() != actions.len()
            || orphan_rows.len() != orphan_frns.len()
            || orphan_rows.windows(2).any(|pair| pair[0] >= pair[1])
            || actions
                .iter()
                .any(|action| *action < -2 || (*action >= 0 && (*action as usize) >= patch_count))
        {
            return 1;
        }
        let builder = SnapshotBuilder::build_compaction(
            ids,
            name_uids,
            flags,
            sizes,
            creation_times,
            last_write_times,
            last_access_times,
            parent_indexes,
            orphan_rows,
            orphan_frns,
            actions,
            patches,
            added,
            unique_count,
            [root_low, root_high],
        );
        unsafe {
            *output = Box::into_raw(Box::new(builder));
        }
        0
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_snapshot_builder_view(
    handle: *mut SnapshotBuilder,
    output: *mut SnapshotView,
) -> i32 {
    result_code(|| {
        if handle.is_null() || output.is_null() {
            return 1;
        }
        unsafe {
            *output = (*handle).view();
        }
        0
    })
}

#[unsafe(no_mangle)]
pub extern "system" fn lertaro_snapshot_builder_free(handle: *mut SnapshotBuilder) {
    if !handle.is_null() {
        unsafe {
            drop(Box::from_raw(handle));
        }
    }
}
