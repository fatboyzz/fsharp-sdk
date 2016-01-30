module Qiniu.CRC32

open System
open System.IO

let mktable (poly : UInt32) =
    let table : UInt32[] = Array.zeroCreate 256
    let rec loop (i : Int32) (j : Int32) (crc : UInt32) =
        match i, j with
        | 256, _ -> table
        | _, 8 -> 
            table.[i] <- crc
            loop (i + 1) 0 (uint32 (i + 1))
        | _, _ -> 
            let nc = crc >>> 1
            loop i (j + 1) (if crc &&& 1u = 1u then nc ^^^ poly else nc)
    loop 0 0 0u

let hash (table : UInt32[]) (crc : UInt32) (input : Stream) =
    let buffered = new BufferedStream(input)
    let rec loop (crc : UInt32) =
        match buffered.ReadByte() with
        | -1 -> ~~~crc
        | v -> loop (table.[int (crc &&& 0xFFu) ^^^ v] ^^^ (crc >>> 8))
    loop ~~~crc

let IEEEPoly = 0xedb88320u;
let IEEETable = mktable IEEEPoly

let hashIEEE (crc : UInt32) (input : Stream) =
     hash IEEETable crc input
