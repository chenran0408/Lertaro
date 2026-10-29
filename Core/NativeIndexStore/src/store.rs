use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::Mutex;

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct RecordInput {
    pub id_low: u64,
    pub id_high: u64,
    pub parent_low: u64,
    pub parent_high: u64,
    pub name_offset: i32,
    pub name_length: i32,
    pub flags: u16,
    pub reserved: u16,
    pub size: i64,
    pub creation_time: u32,
    pub last_write_time: u32,
    pub last_access_time: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct RecordOutput {
    pub id_low: u64,
    pub id_high: u64,
    pub parent_low: u64,
    pub parent_high: u64,
    pub name_id: u32,
    pub flags: u16,
    pub reserved: u16,
    pub size: i64,
    pub creation_time: u32,
    pub last_write_time: u32,
    pub last_access_time: u32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct Record {
    id_low: u64,
    parent_low: u64,
    size: i64,
    creation_time: u32,
    last_write_time: u32,
    last_access_time: u32,
    high_pair_index: u32,
    name_id: u32,
    flags: u16,
    reserved: u16,
}

#[repr(C, packed)]
#[derive(Clone, Copy, Default)]
struct NameSpan {
    offset: u64,
    length: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct HighPair {
    pub id_high: u64,
    pub parent_high: u64,
}

struct RecordStore {
    records: Vec<Record>,
    name_arena: Vec<u16>,
    names: Vec<NameSpan>,
    name_hashes: Vec<u64>,
    name_slots: Vec<u32>,
    high_pairs: Vec<HighPair>,
    sorted_indices: Vec<i32>,
}

impl RecordStore {
    fn new() -> Self {
        Self {
            records: Vec::new(),
            name_arena: Vec::new(),
            names: Vec::new(),
            name_hashes: Vec::new(),
            name_slots: Vec::new(),
            high_pairs: Vec::new(),
            sorted_indices: Vec::new(),
        }
    }

    fn name_hash(name: &[u16]) -> u64 {
        let mut hash = 0xcbf29ce484222325u64;
        for &value in name {
            // Names are UTF-16 throughout this FFI. Hashing the code units directly avoids
            // materializing two little-endian bytes for every character while preserving the
            // collision check in intern_name.
            hash ^= u64::from(value);
            hash = hash.wrapping_mul(0x100000001b3);
        }
        hash
    }

    fn ensure_name_slots(&mut self, incoming: usize) {
        let required = self.names.len().saturating_add(incoming);
        if self.name_slots.len() >= 16
            && required.saturating_mul(10) < self.name_slots.len().saturating_mul(7)
        {
            return;
        }
        let size = required.saturating_mul(2).max(16).next_power_of_two();
        let mut slots = vec![0; size];
        let mask = size - 1;
        for (id, _) in self.names.iter().enumerate() {
            let mut slot = self.name_hashes[id] as usize & mask;
            while slots[slot] != 0 {
                slot = (slot + 1) & mask;
            }
            slots[slot] = id as u32 + 1;
        }
        self.name_slots = slots;
    }

    fn intern_name(&mut self, name: &[u16]) -> u32 {
        let hash = Self::name_hash(name);
        self.ensure_name_slots(1);
        let mask = self.name_slots.len() - 1;
        let mut slot = hash as usize & mask;
        loop {
            let stored = self.name_slots[slot];
            if stored == 0 {
                let id = self.names.len() as u32;
                let offset = self.name_arena.len();
                self.name_arena.extend_from_slice(name);
                self.names.push(NameSpan {
                    offset: offset as u64,
                    length: name.len() as u32,
                });
                self.name_hashes.push(hash);
                self.name_slots[slot] = id + 1;
                return id;
            }
            let id = stored - 1;
            let span = self.names[id as usize];
            let offset = span.offset as usize;
            let length = span.length as usize;
            if &self.name_arena[offset..offset + length] == name {
                return id;
            }
            slot = (slot + 1) & mask;
        }
    }

    fn high_pair_index(&mut self, id_high: u64, parent_high: u64) -> u32 {
        if id_high == 0 && parent_high == 0 {
            return u32::MAX;
        }
        let index = self.high_pairs.len() as u32;
        self.high_pairs.push(HighPair {
            id_high,
            parent_high,
        });
        index
    }

    fn make_record(&mut self, input: RecordInput, name_id: u32) -> Record {
        let high_pair_index = self.high_pair_index(input.id_high, input.parent_high);
        Record {
            id_low: input.id_low,
            parent_low: input.parent_low,
            high_pair_index,
            name_id,
            flags: input.flags,
            reserved: 0,
            size: input.size,
            creation_time: input.creation_time,
            last_write_time: input.last_write_time,
            last_access_time: input.last_access_time,
        }
    }

    fn high_pair(&self, index: u32) -> HighPair {
        if index == u32::MAX {
            HighPair::default()
        } else {
            self.high_pairs[index as usize]
        }
    }

    fn clone_from(&mut self, source: &Self) {
        self.records.clone_from(&source.records);
        self.name_arena.clone_from(&source.name_arena);
        self.names.clone_from(&source.names);
        self.name_hashes.clone_from(&source.name_hashes);
        self.name_slots.clone_from(&source.name_slots);
        self.high_pairs.clone_from(&source.high_pairs);
        self.sorted_indices.clear();
    }

    fn output(&self, index: usize) -> Option<RecordOutput> {
        self.records.get(index).copied().map(|record| {
            let high_pair = self.high_pair(record.high_pair_index);
            RecordOutput {
                id_low: record.id_low,
                id_high: high_pair.id_high,
                parent_low: record.parent_low,
                parent_high: high_pair.parent_high,
                name_id: record.name_id,
                flags: record.flags,
                reserved: record.reserved,
                size: record.size,
                creation_time: record.creation_time,
                last_write_time: record.last_write_time,
                last_access_time: record.last_access_time,
            }
        })
    }

    fn name_ptr(&self, name_id: u32) -> Option<(*const u16, i32)> {
        let span = self.names.get(name_id as usize)?;
        let pointer = unsafe { self.name_arena.as_ptr().add(span.offset as usize) };
        Some((pointer, span.length as i32))
    }

    fn sort_active_indices(&mut self) {
        self.sorted_indices.clear();
        self.sorted_indices.extend(
            self.records
                .iter()
                .enumerate()
                .filter(|(_, r)| r.flags & 2 == 0)
                .map(|(i, _)| i as i32),
        );

        if self.sorted_indices.len() >= 256 {
            self.radix_sort_active_indices();
            return;
        }

        let records = &self.records;
        let high_pairs = &self.high_pairs;
        self.sorted_indices.sort_unstable_by(|left, right| {
            let left_index = *left as usize;
            let right_index = *right as usize;
            let left_row = records[left_index];
            let right_row = records[right_index];
            high_id_for(high_pairs, left_row.high_pair_index)
                .cmp(&high_id_for(high_pairs, right_row.high_pair_index))
                .then_with(|| left_row.id_low.cmp(&right_row.id_low))
                .then_with(|| left_index.cmp(&right_index))
        });
    }

    // Snapshot writes sort every active row by its full ID. A stable LSD radix pass avoids the
    // comparator's repeated high-pair lookups and keeps equal IDs in their original row order.
    fn radix_sort_active_indices(&mut self) {
        let mut source = std::mem::take(&mut self.sorted_indices);
        let mut target = vec![0_i32; source.len()];

        for byte in 0..16 {
            let mut counts = [0_usize; 256];
            for &index in &source {
                let row = self.records[index as usize];
                let high = high_id_for(&self.high_pairs, row.high_pair_index);
                let key = if byte < 8 {
                    row.id_low >> (byte * 8)
                } else {
                    high >> ((byte - 8) * 8)
                };
                counts[(key & 0xff) as usize] += 1;
            }

            let mut offset = 0;
            for count in &mut counts {
                let next = offset + *count;
                *count = offset;
                offset = next;
            }

            for &index in &source {
                let row = self.records[index as usize];
                let high = high_id_for(&self.high_pairs, row.high_pair_index);
                let key = if byte < 8 {
                    row.id_low >> (byte * 8)
                } else {
                    high >> ((byte - 8) * 8)
                };
                let slot = &mut counts[(key & 0xff) as usize];
                target[*slot] = index;
                *slot += 1;
            }

            std::mem::swap(&mut source, &mut target);
        }

        self.sorted_indices = source;
    }
}

fn high_id_for(pairs: &[HighPair], index: u32) -> u64 {
    if index == u32::MAX {
        0
    } else {
        pairs[index as usize].id_high
    }
}

pub struct Handle(Mutex<RecordStore>);

fn with_store<R>(handle: *mut Handle, action: impl FnOnce(&mut RecordStore) -> R) -> Option<R> {
    if handle.is_null() {
        return None;
    }
    let handle = unsafe { &*handle };
    let mut store = handle
        .0
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    Some(action(&mut store))
}

fn result_code(action: impl FnOnce() -> i32) -> i32 {
    catch_unwind(AssertUnwindSafe(action)).unwrap_or(3)
}

#[path = "store/api_core.rs"]
mod api_core;
#[path = "store/api_mutation.rs"]
mod api_mutation;
#[path = "store/api_records.rs"]
mod api_records;
#[cfg(test)]
#[path = "store/tests.rs"]
mod tests;
