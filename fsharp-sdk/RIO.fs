module Qiniu.RIO

open System
open System.IO
open System.Net
open System.Collections
open System.Threading
open Qiniu.Util
open Qiniu.Client
open Qiniu.IO

type ChunkSucc = {
    ctx : String
    checksum : String
    crc32 : Int64
    offset : Int32
    host : String
}

type ChunkRet = 
| ChunkInit 
| ChunkSucc of ChunkSucc 
| ChunkError of Error

type Progress = {
    blockId : Int32
    blockSize : Int32
    ret : ChunkSucc
}
type RPutExtra = {
    customs : (String * String)[]
    mimeType : String
    blockSize : Int32
    chunkSize : Int32
    tryTimes : Int32
    progresses : Progress[]
    notify : Progress -> unit
}

type private RPutParam = {
    c : Client
    token : String
    key : String
    extra : RPutExtra
}

type private BlockCtx = {
    blockId : Int32
    partOffset : Int32
    part : Stream
    ret : ChunkRet
}

let defaultBlockSize = 1 <<< 22 // 4M
let defaultChunkSize = 1 <<< 18 // 256K
let defaultTryTimes = 3

let rputExtra = {
    zero<RPutExtra> with
        blockSize = defaultBlockSize
        chunkSize = defaultChunkSize
        tryTimes = defaultTryTimes
        notify = ignore
}

let private rputParam (c : Client) (token : String) (key : String) (extra : RPutExtra) = {
    c = c; token = token; key = key; extra = extra
}

let parseChunkRet = parse ChunkSucc ChunkError

let private block (param : RPutParam) (ctx : BlockCtx) =
    let blockSize = ctx.partOffset + int32 ctx.part.Length
    let buf : byte[] = Array.zeroCreate param.extra.chunkSize

    let finish (succ : ChunkSucc) =
        succ.offset = blockSize

    let request (url : String) (offset : Int32) =
        let req = request param.c url
        req.Method <- "POST"
        req.ContentType <- "application/octet-stream"
        req.Headers.Add("Authorization", "UpToken " + param.token)
        ctx.part.Seek(int64 (offset - ctx.partOffset), SeekOrigin.Begin) |> ignore
        let size = ctx.part.Read(buf, 0, param.extra.chunkSize)
        req.ContentLength <- int64 size
        let output = req.GetRequestStream()
        output.Write(buf, 0, size)
        req

    let notify (succ : ChunkSucc) =
        param.extra.notify { blockId = ctx.blockId; blockSize = blockSize; ret = succ }

    let rec loop (times : Int32) (prev : ChunkRet) =
        match (times >= param.extra.tryTimes), prev with
        | _, ChunkInit -> 
            let url = String.Format("{0}/mkblk/{1}", param.c.config.upHost, blockSize)
            request url 0 |> response |> parseChunkRet |> loop 0
        | _, ChunkSucc succ when finish succ -> 
            notify succ; prev
        | _, ChunkSucc succ -> 
            notify succ;
            let url = String.Format("{0}/bput/{1}/{2}", succ.host, succ.ctx, succ.offset)
            request url succ.offset |> response |> parseChunkRet |> loop 0
        | false, ChunkError _ -> loop (times + 1) prev
        | true, _ -> prev
    
    loop 0 ctx.ret


let private mkfile (param : RPutParam) (total : Int64) (ctxs : String seq) =
    let request _ =
        let customUri (key : String, value : String) =
            String.Format("/{0}/{1}", key, value)
        let mime = param.extra.mimeType
        let url = 
            [|
                param.c.config.upHost
                "/mkfile/" + total.ToString()
                "/key/" + (param.key |> stringToBase64Safe)
                (if nullOrEmpty mime then "" else "/mimeType/" + stringToBase64Safe mime)
                param.extra.customs |> Seq.map customUri |> String.Concat
            |] |> String.Concat
        let req = request param.c url
        req.Method <- "POST"
        req.ContentType <- "text/plain"
        req.Headers.Add("Authorization", "UpToken " + param.token)
        let stream = req.GetRequestStream()
        let body = ctxs |> String.concat "," |> stringToUtf8
        stream.Write(body, 0, body.Length)
        req
    request() |> response |> parse PutSucc PutError


let private doRput (param : RPutParam) (input : Stream) =
    let blockSize = param.extra.blockSize
    let blockCount = ((input.Length + int64 blockSize - 1L) / int64 blockSize) |> int32
    let blockLast = (input.Length - int64 blockSize * int64 (blockCount - 1)) |> int32
    let revProgresses = param.extra.progresses |> Array.rev

    let finish (blockId : Int32) (succ : ChunkSucc) =
        succ.offset = blockSize || (blockId = blockCount - 1 && succ.offset = blockLast)

    let partLockObject = obj()
    let partAt (inputOffset : Int64) (length : Int32) =
        lock partLockObject (fun _ -> 
            let buf : byte[] = Array.zeroCreate length
            input.Seek(inputOffset, SeekOrigin.Begin) |> ignore
            let lengthRead = input.Read(buf, 0, length)
            new MemoryStream(buf, 0, lengthRead)
        ) 
    
    let work (blockId : Int32) =
        let inputOffset = int64 blockId * int64 blockSize
        let progress = revProgresses |> Array.tryFind (fun p -> p.blockId = blockId) 
        match progress with
        | Some p when finish blockId p.ret ->
            ChunkSucc p.ret
        | Some p -> 
            let part = partAt (inputOffset + int64 p.ret.offset) (blockSize - p.ret.offset)
            let ctx = { blockId = blockId; partOffset = p.ret.offset; part = part; ret = ChunkSucc p.ret }
            block param ctx
        | None -> 
            let part = partAt inputOffset blockSize
            let ctx = { blockId = blockId; partOffset = 0; part = part; ret = ChunkInit }
            block param ctx

    let workAsync (blockId : Int32) = async { return work blockId }

    seq { 0 .. blockCount - 1 }
    |> Seq.map work
    |> Seq.map (fun r ->
        match r with
        | ChunkSucc succ -> succ.ctx
        | _ -> ""
    )
    |> mkfile param input.Length


let rput (c : Client) (token : String) (key : String) (input : Stream) (extra : RPutExtra) =
    doRput (rputParam c token key extra) input    
