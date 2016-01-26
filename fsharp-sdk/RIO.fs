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
    worker : Int32
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

let rputExtra = {
    zero<RPutExtra> with
        blockSize = 1 <<< 22 // 4M
        chunkSize = 1 <<< 18 // 256K
        tryTimes = 3
        worker = 4
        notify = ignore
}

let parseChunkRet = parse ChunkSucc ChunkError

let private block (param : RPutParam) (ctx : BlockCtx) =
    let blockSize = ctx.partOffset + int32 ctx.part.Length
    let buf : byte[] = Array.zeroCreate param.extra.chunkSize

    let finish (succ : ChunkSucc) =
        succ.offset = blockSize

    let request (url : String) (offset : Int32) =
        async {
            let req = request url
            req.Method <- "POST"
            req.ContentType <- "application/octet-stream"
            req.Headers.Add(HttpRequestHeader.Authorization, "UpToken " + param.token)
            ctx.part.Position <- int64 (offset - ctx.partOffset)
            let size = ctx.part.Read(buf, 0, param.extra.chunkSize)
            req.ContentLength <- int64 size
            use! output = requestStream req
            output.Write(buf, 0, size)
            return req
        }

    let notify (succ : ChunkSucc) =
        param.extra.notify { blockId = ctx.blockId; blockSize = blockSize; ret = succ }

    let rec loop (times : Int32) (notifyPrev : bool) (prev : ChunkRet) =
        match (times >= param.extra.tryTimes), prev with
        | _, ChunkInit -> 
            let url = String.Format("{0}/mkblk/{1}", param.c.config.upHost, blockSize)
            request url 0 |!> responseJson |>> parseChunkRet |!> loop 0 true
        | _, ChunkSucc succ when finish succ -> 
            if notifyPrev then notify succ
            async { return prev }
        | _, ChunkSucc succ -> 
            if notifyPrev then notify succ
            let url = String.Format("{0}/bput/{1}/{2}", succ.host, succ.ctx, succ.offset)
            request url succ.offset |!> responseJson |>> parseChunkRet |!> loop 0 true
        | false, ChunkError _ -> 
            async { return! loop (times + 1) true prev }
        | true, _ -> async { return prev }
    
    loop 0 false ctx.ret 

let private mkfile (param : RPutParam) (total : Int64) (ctxs : String seq) =
    async {
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
            |] |> concat
        let req = request url
        req.Method <- "POST"
        req.ContentType <- "text/plain"
        req.Headers.Add(HttpRequestHeader.Authorization, "UpToken " + param.token)
        use! output = requestStream req
        let body = ctxs |> String.concat "," |> stringToUtf8
        output.Write(body, 0, body.Length)
        return req
    } |!> responseJson |>> parsePutRet

let private doRput (param : RPutParam) (input : Stream) =
    let blockSize = param.extra.blockSize
    let blockCount = int32 ((input.Length + int64 blockSize - 1L) / int64 blockSize)
    let blockLast = int32 (input.Length - int64 blockSize * int64 (blockCount - 1))
    let revProgresses = param.extra.progresses |> Array.rev

    let finish (blockId : Int32) (succ : ChunkSucc) =
        succ.offset = blockSize || (blockId = blockCount - 1 && succ.offset = blockLast)

    let partLockObject = new Object()
    let partAt (inputOffset : Int64) (length : Int32) =
        lock partLockObject (fun _ -> 
            let buf : byte[] = Array.zeroCreate length
            input.Position <- inputOffset
            let lengthRead = input.Read(buf, 0, length)
            new MemoryStream(buf, 0, lengthRead)
        ) 
    
    let work (blockId : Int32) =
        async {
            let inputOffset = int64 blockId * int64 blockSize
            let progress = revProgresses |> Array.tryFind (fun p -> p.blockId = blockId) 
            match progress with
            | Some p when finish blockId p.ret ->
                return ChunkSucc p.ret
            | Some p -> 
                let part = partAt (inputOffset + int64 p.ret.offset) (blockSize - p.ret.offset)
                let ctx = { blockId = blockId; partOffset = p.ret.offset; part = part; ret = ChunkSucc p.ret }
                return! block param ctx
            | None -> 
                let part = partAt inputOffset blockSize
                let ctx = { blockId = blockId; partOffset = 0; part = part; ret = ChunkInit }
                return! block param ctx
        }

    let pickctx (r : ChunkRet) =
        match r with
        | ChunkSucc succ -> succ.ctx
        | _ -> ""

    async {
        let! rets = [| 0 .. blockCount - 1 |]
                    |> Array.map work
                    |> limitedParallel param.extra.worker
        let ctxs = Array.map pickctx rets
        if Array.exists nullOrEmpty ctxs then
            return PutError({ error = "Block not all done" })
        else return! mkfile param input.Length ctxs
    }


let rput (c : Client) (token : String) (key : String) (input : Stream) (extra : RPutExtra) =
    async { return! doRput { c = c; token = token; key = key; extra = extra } input }
    
