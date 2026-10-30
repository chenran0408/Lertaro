use super::*;
use std::collections::{HashMap, VecDeque};

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct CompactionPatch {
    pub parent_low: u64,
    pub parent_high: u64,
    pub name_uid: u32,
    pub flags: u16,
    pub reserved: u16,
    pub size: i64,
    pub creation_time: u32,
    pub last_write_time: u32,
    pub last_access_time: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct CompactionRecord {
    pub id_low: u64,
    pub id_high: u64,
    pub parent_low: u64,
    pub parent_high: u64,
    pub name_uid: u32,
    pub flags: u16,
    pub reserved: u16,
    pub size: i64,
    pub creation_time: u32,
    pub last_write_time: u32,
    pub last_access_time: u32,
}

#[derive(Clone, Copy)]
struct SnapshotRow {
    id: [u64; 2],
    parent: [u64; 2],
    name_uid: u32,
    flags: u16,
    size: i64,
    creation_time: u32,
    last_write_time: u32,
    last_access_time: u32,
}

#[repr(C)]
pub struct SnapshotView {
    pub name_ids: *const u32,
    pub flags: *const u16,
    pub parent_indexes: *const i32,
    pub ids: *const [u64; 2],
    pub sizes: *const i64,
    pub recursive_sizes: *const i64,
    pub creation_times: *const u32,
    pub last_write_times: *const u32,
    pub last_access_times: *const u32,
    pub child_starts: *const i32,
    pub children: *const i32,
    pub uid_starts: *const i32,
    pub uid_rows: *const i32,
    pub orphan_rows: *const i32,
    pub orphan_frns: *const [u64; 2],
    pub unique_name_ids: *const u32,
    pub row_count: i32,
    pub unique_count: i32,
    pub children_count: i32,
    pub orphan_count: i32,
    pub total_files: i32,
    pub total_dirs: i32,
}

pub struct SnapshotBuilder {
    name_ids: Vec<u32>,
    flags: Vec<u16>,
    parents: Vec<[u64; 2]>,
    parent_indexes: Vec<i32>,
    ids: Vec<[u64; 2]>,
    sizes: Vec<i64>,
    recursive_sizes: Vec<i64>,
    creation_times: Vec<u32>,
    last_write_times: Vec<u32>,
    last_access_times: Vec<u32>,
    child_starts: Vec<i32>,
    children: Vec<i32>,
    uid_starts: Vec<i32>,
    uid_rows: Vec<i32>,
    orphan_rows: Vec<i32>,
    orphan_frns: Vec<[u64; 2]>,
    unique_name_ids: Vec<u32>,
    total_files: i32,
    total_dirs: i32,
}

impl SnapshotBuilder {
    pub(super) fn build(store: &mut RecordStore, root_id: [u64; 2]) -> Self {
        store.sort_active_indices();
        let count = store.sorted_indices.len();
        let mut source_name_ids = Vec::new();
        let mut uid_by_name = vec![u32::MAX; store.names.len()];
        let mut builder = Self::new(count, 0);
        for &index in &store.sorted_indices {
            let row = store.records[index as usize];
            let high = store.high_pair(row.high_pair_index);
            let uid = match uid_by_name[row.name_id as usize] {
                u32::MAX => {
                    let uid = source_name_ids.len() as u32;
                    uid_by_name[row.name_id as usize] = uid;
                    source_name_ids.push(row.name_id);
                    uid
                }
                uid => uid,
            };
            builder.push_row(SnapshotRow {
                id: [row.id_low, high.id_high],
                parent: [row.parent_low, high.parent_high],
                name_uid: uid,
                flags: row.flags,
                size: row.size,
                creation_time: row.creation_time,
                last_write_time: row.last_write_time,
                last_access_time: row.last_access_time,
            });
        }
        builder.unique_name_ids = source_name_ids;
        builder.finish_indexes(root_id);
        builder
    }

    pub(super) fn build_compaction(
        base_ids: &[[u64; 2]],
        base_name_uids: &[u32],
        base_flags: &[u16],
        base_sizes: &[i64],
        base_creation_times: &[u32],
        base_last_write_times: &[u32],
        base_last_access_times: &[u32],
        base_parent_indexes: &[i32],
        orphan_rows: &[i32],
        orphan_frns: &[[u64; 2]],
        actions: &[i32],
        patches: &[CompactionPatch],
        added: &[CompactionRecord],
        unique_count: usize,
        root_id: [u64; 2],
    ) -> Self {
        let mut added_rows = Vec::with_capacity(added.len());
        for record in added {
            added_rows.push(SnapshotRow {
                id: [record.id_low, record.id_high],
                parent: [record.parent_low, record.parent_high],
                name_uid: record.name_uid,
                flags: record.flags,
                size: record.size,
                creation_time: record.creation_time,
                last_write_time: record.last_write_time,
                last_access_time: record.last_access_time,
            });
        }
        // The base rows are already sorted and remain sorted after filtering. Sort only the small
        // added set, then merge it with the base stream instead of sorting the whole snapshot again.
        // Equal IDs deliberately keep base rows before added rows, matching the previous stable sort.
        added_rows.sort_by(compare_ids);
        let mut added_index = 0;
        let mut rows = Vec::with_capacity(base_ids.len() + added_rows.len());
        for row in 0..base_ids.len() {
            let action = actions[row];
            while added_index < added_rows.len()
                && compare_id_to(added_rows[added_index].id, base_ids[row])
                    == std::cmp::Ordering::Less
            {
                rows.push(added_rows[added_index]);
                added_index += 1;
            }
            if action == -2 {
                continue;
            }
            if action >= 0 {
                let patch = patches[action as usize];
                rows.push(SnapshotRow {
                    id: base_ids[row],
                    parent: [patch.parent_low, patch.parent_high],
                    name_uid: patch.name_uid,
                    flags: patch.flags,
                    size: patch.size,
                    creation_time: patch.creation_time,
                    last_write_time: patch.last_write_time,
                    last_access_time: patch.last_access_time,
                });
            } else {
                rows.push(SnapshotRow {
                    id: base_ids[row],
                    parent: base_parent(
                        row,
                        base_ids,
                        base_parent_indexes,
                        orphan_rows,
                        orphan_frns,
                    ),
                    name_uid: base_name_uids[row],
                    flags: base_flags[row],
                    size: base_sizes[row],
                    creation_time: base_creation_times[row],
                    last_write_time: base_last_write_times[row],
                    last_access_time: base_last_access_times[row],
                });
            }
        }
        rows.extend(added_rows[added_index..].iter().copied());
        Self::build_from_rows(rows, unique_count, root_id)
    }

    fn build_from_rows(rows: Vec<SnapshotRow>, unique_count: usize, root_id: [u64; 2]) -> Self {
        let count = rows.len();
        let mut builder = Self::new(count, unique_count);
        for row in rows {
            builder.push_row(row);
        }
        builder.finish_indexes(root_id);
        builder
    }

    fn new(row_capacity: usize, unique_count: usize) -> Self {
        Self {
            name_ids: Vec::with_capacity(row_capacity),
            flags: Vec::with_capacity(row_capacity),
            parents: Vec::with_capacity(row_capacity),
            parent_indexes: Vec::with_capacity(row_capacity),
            ids: Vec::with_capacity(row_capacity),
            sizes: Vec::with_capacity(row_capacity),
            recursive_sizes: Vec::with_capacity(row_capacity),
            creation_times: Vec::with_capacity(row_capacity),
            last_write_times: Vec::with_capacity(row_capacity),
            last_access_times: Vec::with_capacity(row_capacity),
            child_starts: Vec::new(),
            children: Vec::new(),
            uid_starts: Vec::new(),
            uid_rows: Vec::with_capacity(row_capacity),
            orphan_rows: Vec::new(),
            orphan_frns: Vec::new(),
            unique_name_ids: (0..unique_count as u32).collect(),
            total_files: 0,
            total_dirs: 0,
        }
    }

    fn push_row(&mut self, row: SnapshotRow) {
        self.name_ids.push(row.name_uid);
        self.flags.push(row.flags);
        self.parents.push(row.parent);
        self.ids.push(row.id);
        self.sizes.push(row.size);
        self.creation_times.push(row.creation_time);
        self.last_write_times.push(row.last_write_time);
        self.last_access_times.push(row.last_access_time);
        self.recursive_sizes.push(0);
        if row.flags & 1 != 0 {
            self.total_dirs += 1;
        } else {
            self.total_files += 1;
        }
    }

    fn finish_indexes(&mut self, root_id: [u64; 2]) {
        let count = self.ids.len();
        self.parent_indexes.resize(count, -1);
        self.recursive_sizes.resize(count, 0);
        let mut record_index_map = None;
        for row in 0..count {
            let parent = self.parents[row];
            if parent != self.ids[row] {
                let parent_index = resolve_parent(&self.ids, parent, &mut record_index_map);
                if parent_index >= 0 {
                    self.parent_indexes[row] = parent_index;
                } else {
                    self.orphan_rows.push(row as i32);
                    self.orphan_frns.push(parent);
                }
            }
        }

        self.child_starts = vec![0; count + 1];
        for &parent in &self.parent_indexes {
            if parent >= 0 {
                self.child_starts[parent as usize + 1] += 1;
            }
        }
        for row in 0..count {
            self.child_starts[row + 1] += self.child_starts[row];
        }
        self.children = vec![0; self.child_starts[count] as usize];
        let mut child_cursor = self.child_starts.clone();
        for row in 0..count {
            let parent = self.parent_indexes[row];
            if parent >= 0 {
                let slot = child_cursor[parent as usize] as usize;
                self.children[slot] = row as i32;
                child_cursor[parent as usize] += 1;
            }
        }

        build_recursive_sizes(self, root_id);
        self.uid_starts = vec![0; self.unique_name_ids.len() + 1];
        for &uid in &self.name_ids {
            self.uid_starts[uid as usize + 1] += 1;
        }
        for uid in 0..self.unique_name_ids.len() {
            self.uid_starts[uid + 1] += self.uid_starts[uid];
        }
        self.uid_rows = vec![0; count];
        let mut uid_cursor = self.uid_starts.clone();
        for (row, &uid) in self.name_ids.iter().enumerate() {
            let slot = uid_cursor[uid as usize] as usize;
            self.uid_rows[slot] = row as i32;
            uid_cursor[uid as usize] += 1;
        }
        self.parents.clear();
        self.parents.shrink_to_fit();
    }

    pub(super) fn view(&self) -> SnapshotView {
        SnapshotView {
            name_ids: self.name_ids.as_ptr(),
            flags: self.flags.as_ptr(),
            parent_indexes: self.parent_indexes.as_ptr(),
            ids: self.ids.as_ptr(),
            sizes: self.sizes.as_ptr(),
            recursive_sizes: self.recursive_sizes.as_ptr(),
            creation_times: self.creation_times.as_ptr(),
            last_write_times: self.last_write_times.as_ptr(),
            last_access_times: self.last_access_times.as_ptr(),
            child_starts: self.child_starts.as_ptr(),
            children: self.children.as_ptr(),
            uid_starts: self.uid_starts.as_ptr(),
            uid_rows: self.uid_rows.as_ptr(),
            orphan_rows: self.orphan_rows.as_ptr(),
            orphan_frns: self.orphan_frns.as_ptr(),
            unique_name_ids: self.unique_name_ids.as_ptr(),
            row_count: self.name_ids.len().min(i32::MAX as usize) as i32,
            unique_count: self.unique_name_ids.len().min(i32::MAX as usize) as i32,
            children_count: self.children.len().min(i32::MAX as usize) as i32,
            orphan_count: self.orphan_rows.len().min(i32::MAX as usize) as i32,
            total_files: self.total_files,
            total_dirs: self.total_dirs,
        }
    }
}

fn compare_ids(left: &SnapshotRow, right: &SnapshotRow) -> std::cmp::Ordering {
    compare_id_to(left.id, right.id)
}

fn compare_id_to(left: [u64; 2], right: [u64; 2]) -> std::cmp::Ordering {
    left[1].cmp(&right[1]).then_with(|| left[0].cmp(&right[0]))
}

fn base_parent(
    row: usize,
    ids: &[[u64; 2]],
    parent_indexes: &[i32],
    orphan_rows: &[i32],
    orphan_frns: &[[u64; 2]],
) -> [u64; 2] {
    let parent = parent_indexes[row];
    if parent >= 0 {
        return ids[parent as usize];
    }
    match orphan_rows.binary_search(&(row as i32)) {
        Ok(index) => orphan_frns[index],
        Err(_) => ids[row],
    }
}

fn resolve_parent(
    ids: &[[u64; 2]],
    parent: [u64; 2],
    record_index_map: &mut Option<HashMap<u64, i32>>,
) -> i32 {
    if let Some(index) = first_row_for_id(ids, parent) {
        return index;
    }
    let map = record_index_map.get_or_insert_with(|| {
        let mut map = HashMap::with_capacity(ids.len());
        for (index, id) in ids.iter().enumerate() {
            map.entry(id[0] & 0x0000_FFFF_FFFF_FFFF)
                .or_insert(index as i32);
        }
        map
    });
    map.get(&(parent[0] & 0x0000_FFFF_FFFF_FFFF))
        .copied()
        .unwrap_or(-1)
}

fn first_row_for_id(ids: &[[u64; 2]], id: [u64; 2]) -> Option<i32> {
    let mut low = 0;
    let mut high = ids.len();
    while low < high {
        let middle = low + (high - low) / 2;
        if ids[middle][1] < id[1] || (ids[middle][1] == id[1] && ids[middle][0] < id[0]) {
            low = middle + 1;
        } else {
            high = middle;
        }
    }
    (low < ids.len() && ids[low] == id).then_some(low as i32)
}

fn build_recursive_sizes(builder: &mut SnapshotBuilder, root_id: [u64; 2]) {
    let root = first_row_for_id(&builder.ids, root_id)
        .map(|row| row as i32)
        .unwrap_or(-1);
    let mut remaining = vec![0_i32; builder.ids.len()];
    for row in 0..builder.ids.len() {
        let is_directory = builder.flags[row] & 1 != 0;
        if !is_directory && (row == 0 || builder.ids[row - 1] != builder.ids[row]) {
            builder.recursive_sizes[row] = builder.sizes[row].max(0);
        }
        if let Some(parent) = effective_parent(row as i32, &builder.parent_indexes, root) {
            remaining[parent as usize] += 1;
        }
    }
    let mut queue = VecDeque::new();
    for (row, &children) in remaining.iter().enumerate() {
        if children == 0 {
            queue.push_back(row as i32);
        }
    }
    while let Some(row) = queue.pop_front() {
        let Some(parent) = effective_parent(row, &builder.parent_indexes, root) else {
            continue;
        };
        builder.recursive_sizes[parent as usize] = builder.recursive_sizes[parent as usize]
            .saturating_add(builder.recursive_sizes[row as usize]);
        remaining[parent as usize] -= 1;
        if remaining[parent as usize] == 0 {
            queue.push_back(parent);
        }
    }
}

fn effective_parent(row: i32, parent_indexes: &[i32], root: i32) -> Option<i32> {
    let mut parent = parent_indexes[row as usize];
    if parent < 0 && row != root {
        parent = root;
    }
    (parent >= 0 && (parent as usize) < parent_indexes.len() && parent != row).then_some(parent)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn add(store: &mut RecordStore, id: u64, parent: u64, flags: u16, size: i64, name: &[u16]) {
        let name_id = store.intern_name(name);
        let record = store.make_record(
            RecordInput {
                id_low: id,
                parent_low: parent,
                flags,
                size,
                ..RecordInput::default()
            },
            name_id,
        );
        store.records.push(record);
    }

    #[test]
    fn builds_sorted_columns_parent_tree_and_recursive_sizes() {
        let mut store = RecordStore::new();
        add(&mut store, 3, 2, 0, 7, &[3]);
        add(&mut store, 1, 1, 1, 0, &[1]);
        add(&mut store, 2, 1, 1, 0, &[2]);
        add(&mut store, 4, 2, 0, 5, &[4]);
        let snapshot = SnapshotBuilder::build(&mut store, [1, 0]);
        assert_eq!(
            snapshot.ids.iter().map(|id| id[0]).collect::<Vec<_>>(),
            [1, 2, 3, 4]
        );
        assert_eq!(snapshot.parent_indexes, [-1, 0, 1, 1]);
        assert_eq!(snapshot.recursive_sizes, [12, 12, 7, 5]);
        assert_eq!(snapshot.uid_starts, [0, 1, 2, 3, 4]);
    }

    #[test]
    fn compaction_merges_added_rows_after_sorted_base_rows() {
        let base_ids = [[1, 0], [3, 0], [3, 0], [5, 0]];
        let base_name_uids = [0, 1, 2, 3];
        let base_flags = [1, 0, 0, 0];
        let base_sizes = [0, 10, 20, 30];
        let base_times = [0; 4];
        let base_parent_indexes = [-1, 0, 0, 0];
        let added = [
            CompactionRecord {
                id_low: 4,
                parent_low: 1,
                name_uid: 4,
                ..Default::default()
            },
            CompactionRecord {
                id_low: 3,
                parent_low: 1,
                name_uid: 5,
                ..Default::default()
            },
            CompactionRecord {
                id_low: 2,
                parent_low: 1,
                name_uid: 6,
                ..Default::default()
            },
            CompactionRecord {
                id_low: 3,
                parent_low: 1,
                name_uid: 7,
                ..Default::default()
            },
        ];
        let snapshot = SnapshotBuilder::build_compaction(
            &base_ids,
            &base_name_uids,
            &base_flags,
            &base_sizes,
            &base_times,
            &base_times,
            &base_times,
            &base_parent_indexes,
            &[],
            &[],
            &[-1, -1, -1, -1],
            &[],
            &added,
            8,
            [1, 0],
        );

        assert_eq!(
            snapshot.ids.iter().map(|id| id[0]).collect::<Vec<_>>(),
            [1, 2, 3, 3, 3, 3, 4, 5]
        );
        assert_eq!(snapshot.name_ids, [0, 6, 1, 2, 5, 7, 4, 3]);
    }
}
