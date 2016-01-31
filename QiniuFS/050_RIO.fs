module QiniuFS.RIO

open System
open System.IO
open System.Net
open System.Collections
open System.Threading
open Util
open Client
open IO

type ChunkSucc = {
    ctx : String
    checksum : String
    crc32 : Int64
    offset : Int32
    host : String
}

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
    bufSize : Int32
    tryTimes : Int32
    worker : Int32
    progresses : Progress[]
    notify : Progress -> unit
}

let rputExtra = {
    zero<RPutExtra> with
        blockSize = 1 <<< 22 // 4M
        chunkSize = 1 <<< 20 // 1M
        bufSize = 1 <<< 15 // 32K
        tryTimes = 3
        worker = 4
        notify = ignore
}

type private RPutParam = {
    c : Client
    token : String
    key : String
    extra : RPutExtra
}

type private ChunkRet = 
| ChunkInit 
| ChunkSucc of ChunkSucc 
| ChunkError of Error

type private BlockCtx = {
    blockId : Int32
    blockSize : Int32
    prev : ChunkRet
    readAt : Int64 -> Int32 -> byte[]
    notify : Progress -> unit
}

let cleanProgresses (ps : Progress[]) =
    let rec loop (acc : Progress list) (rps : Progress list) =
        match rps with
        | [] -> acc |> List.rev |> List.toArray
        | head :: tail -> 
            match acc |> List.tryFind (fun p -> p.blockId = head.blockId) with
            | Some p -> loop acc tail
            | None -> loop (head :: acc) tail
    loop [] (ps |> Array.rev |> Array.toList)

let private parseChunkRet = parse ChunkSucc ChunkError

let private block (param : RPutParam) (ctx : BlockCtx) =
    let extra = param.extra
    let blockStart = int64 extra.blockSize * int64 ctx.blockId 
    let buf = lazy Array.zeroCreate extra.bufSize

    let put (url : String) (offset : Int32) =
        async {
            let req = request url
            req.Method <- "POST"
            req.ContentType <- "application/octet-stream"
            req.Headers.Add(HttpRequestHeader.Authorization, "UpToken " + param.token)
            let start = blockStart + int64 offset
            let length = min extra.chunkSize (ctx.blockSize - offset)
            req.ContentLength <- int64 length
            let data = ctx.readAt start length
            let input = new MemoryStream(data)
            use! output = requestStream req
            do! asyncCopy (buf.Force()) input output
            let! ret = req |> responseJson |>> parseChunkRet
            match ret with
            | ChunkSucc succ -> 
                ctx.notify { blockId = ctx.blockId; blockSize = ctx.blockSize; ret = succ }
            | _ -> ()
            return ret
        }

    let rec loop (times : Int32) (prev : ChunkRet) =
        match (times >= extra.tryTimes), prev with
        | _, ChunkInit -> 
            let url = String.Format("{0}/mkblk/{1}", param.c.config.upHost, ctx.blockSize)
            put url 0 |!> loop 0
        | _, ChunkSucc succ when succ.offset = ctx.blockSize  -> 
            async { return prev }
        | _, ChunkSucc succ -> 
            let url = String.Format("{0}/bput/{1}/{2}", succ.host, succ.ctx, succ.offset)
            put url succ.offset |!> loop 0
        | false, ChunkError _ -> 
            async { return! loop (times + 1) prev }
        | true, _ -> 
            async { return prev }
    
    loop 0 ctx.prev

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
                param.extra.customs |> Array.map customUri |> String.Concat
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
    let extra = param.extra
    let blockSize = extra.blockSize
    let blockCount = int32 ((input.Length + int64 blockSize - 1L) / int64 blockSize)
    let blockSizeOfId (blockId : Int32) =
        if blockId < blockCount - 1 then blockSize 
        else int32 (input.Length - int64 blockSize * int64 (blockCount - 1))

    let progresses = extra.progresses |> cleanProgresses

    let inputLock = new Object()
    let readAt (offset : Int64) (length : Int32) =
        lock inputLock (fun _ -> 
            let buf : byte[] = Array.zeroCreate length
            input.Position <- offset
            input.Read(buf, 0, length) |> ignore
            buf
        ) 

    let notifyLock = new Object()
    let notify (p : Progress) =
        lock notifyLock (fun _ -> extra.notify p)
    
    let work (blockId : Int32) =
        async {
            let blockCtx (prev : ChunkRet) = { 
                blockId = blockId; blockSize = blockSizeOfId blockId; prev = prev; 
                readAt = readAt; notify = notify 
            }
            let progress = progresses |> Array.tryFind (fun p -> p.blockId = blockId) 
            match progress with
            | Some p -> return! blockCtx (ChunkSucc p.ret) |> block param
            | None -> return! blockCtx ChunkInit |> block param
        }

    let pickctx (r : ChunkRet) =
        match r with
        | ChunkSucc succ -> succ.ctx
        | _ -> ""

    async {
        let! rets = [| 0 .. blockCount - 1 |]
                    |> Array.map work
                    |> limitedParallel extra.worker
        let ctxs = Array.map pickctx rets
        if Array.forall (nullOrEmpty >> not) ctxs then
            return! mkfile param input.Length ctxs
        else return PutError({ error = "Block not all done" })
    }

let rput (c : Client) (token : String) (key : String) (input : Stream) (extra : RPutExtra) =
    async { return! doRput { c = c; token = token; key = key; extra = extra } input }
    
