module QiniuFS.QETag

open System
open System.Security.Cryptography
open System.IO
open Util

let inline sha1Bytes (bs : byte[]) =
    use h = SHA1.Create()
    h.ComputeHash(bs)

let inline sha1Stream (input : Stream) =
    use h = SHA1.Create()
    h.ComputeHash input

let private hashSmall (input : Stream) = 
    let bs = sha1Stream input
    [| [| 0x16uy |]; bs |] 
    |> concatBytes 
    |> Base64Safe.fromBytes

let private hashBig (input : Stream) =
    let worker = Environment.ProcessorCount
    let blockSize = 1 <<< 22 // 4M
    let blockCount = int32 (((input.Length + int64 blockSize - 1L) / int64 blockSize))
    let readAt = readerAt input
    let work (blockId : Int32) =
        async {
            let blockStart = int64 blockId * int64 blockSize
            return readAt blockStart blockSize |> sha1Stream
        }
    async {
        let! rets = [| 0 .. blockCount - 1 |]
                    |> Array.map work
                    |> limitedParallel worker
        let bs = rets |> concatBytes |> sha1Bytes
        return [| [| 0x96uy |]; bs |] 
               |> concatBytes 
               |> Base64Safe.fromBytes
    }

let hashThreshold = 1L <<< 22 // 4M
let hash (input : Stream) = 
    if input.Length <= hashThreshold 
    then hashSmall input 
    else hashBig input |> Async.RunSynchronously

let inline hashFile (path : String) =
    use input = File.OpenRead(path)
    hash input
