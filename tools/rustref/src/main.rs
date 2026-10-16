//! Usage: cargo run --release -- "<Ultima V dir>" > ../../tests/U5InValheim.Tests/golden/rustref.tsv
//! Output lines: kind <TAB> name <TAB> length <TAB> fnv1a64 (hex). Only hashes leave this tool.
#[path = "../../../../Ultima5Revolutions/rust/src/lzw.rs"]
mod lzw;

use std::path::Path;

fn fnv1a64(data: &[u8]) -> u64 {
    let mut h: u64 = 0xcbf29ce484222325;
    for &b in data {
        h ^= b as u64;
        h = h.wrapping_mul(0x100000001b3);
    }
    h
}

const LZW_FILES: &[&str] = &[
    "TILES.16", "ITEMS.16", "DNG1.16", "DNG2.16", "DNG3.16", "MON0.16", "MON1.16", "MON2.16", "MON3.16",
    "MON4.16", "MON5.16", "MON6.16", "MON7.16", "CREATE.16", "TEXT.16", "STORY1.16", "STORY2.16",
    "STORY3.16", "STORY4.16", "STORY5.16", "STORY6.16", "END1.16", "END2.16", "ULTIMA.16", "STARTSC.16",
    "ENDSC.16", "TITLE.BIT", "BRITISH.BIT", "PROPORT.PCS",
];

fn main() {
    let dir = std::env::args().nth(1).expect("usage: u5ref <Ultima V dir>");
    let dir = Path::new(&dir);
    for name in LZW_FILES {
        let raw = match std::fs::read(dir.join(name)) {
            Ok(r) => r,
            Err(_) => continue,
        };
        let out = lzw::decompress_file(&raw).unwrap_or_else(|e| panic!("{name}: {e}"));
        println!("lzw\t{name}\t{}\t{:016x}", out.len(), fnv1a64(&out));
    }
}
