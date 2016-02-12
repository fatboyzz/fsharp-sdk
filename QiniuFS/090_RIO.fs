module QiniuFS.RIO

open System
open System.IO
open System.Net
open System.Collections
open System.Threading
open IO

type ChunkSucc = {
    ctx : String
    checksum : String
    crc32 : UInt32
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
    customs = Array.empty
    mimeType = ""
    blockSize = 1 <<< 22 // 4M
    chunkSize = 1 <<< 20 // 1M
    bufSize = 1 <<< 15 // 32K
    tryTimes = 3
    worker = 4
    progresses = Array.empty
    notify = ignore
}

type private RPutParam = {
    c : Client
    token : String
    key : String
    extra : RPutExtra
}

type private BlockCtx = {
    blockId : Int32
    blockSize : Int32
    prev : ChunkSucc
    readAt : Int64 -> Int32 -> MemoryStream
    notify : Progress -> unit
}

let chunkSucc = Zero.instance<ChunkSucc>

let cleanProgresses (ps : Progress[]) =
    ps
    |> Array.rev
    |> Seq.distinctBy (fun p -> p.blockId)
    |> Seq.toArray
    |> Array.rev
    
let private block (param : RPutParam) (ctx : BlockCtx) =
    let extra = param.extra
    let blockStart = int64 extra.blockSize * int64 ctx.blockId 
    let buf = lazy Array.zeroCreate extra.bufSize

    let put (url : String) (offset : Int32) =
        async {
            let req = requestUrl url
            req.Method <- "POST"
            req.ContentType <- "application/octet-stream"
            req.Headers.Add(HttpRequestHeader.Authorization, "UpToken " + param.token)
            let start = blockStart + int64 offset
            let length = min extra.chunkSize (ctx.blockSize - offset)
            req.ContentLength <- int64 length

            let input = ctx.readAt start length
            let crc32 = CRC32.hashIEEE 0u input
            input.Position <- 0L

            use! output = requestStream req
            do! asyncCopy (buf.Force()) input output
            let! ret = req |> responseJson<ChunkSucc>
            match ret with
            | Succ succ when succ.crc32 = crc32 -> 
                ctx.notify { blockId = ctx.blockId; blockSize = ctx.blockSize; ret = succ }; 
                return ret
            | Succ succ ->
                return Error { error = "Invalid chunk crc32" }
            | _ -> return ret
        }

    let rec loop (times : Int32) (prev : ChunkSucc) (cur : Ret<ChunkSucc>) =
        async {
            match (times >= extra.tryTimes), cur with
            | _, Succ c when c.offset = 0 -> 
                let url = String.Format("{0}/mkblk/{1}", param.c.config.upHost, ctx.blockSize)
                return! put url 0 |!> loop times c
            | _, Succ c when c.offset = ctx.blockSize -> 
                return cur 
            | _, Succ c -> 
                let url = String.Format("{0}/bput/{1}/{2}", c.host, c.ctx, c.offset)
                return! put url c.offset |!> loop times c
            | false, Error _ -> 
                return! loop (times + 1) prev (Succ prev)
            | true, _ -> 
                return cur
        }

    loop 0 ctx.prev (Succ ctx.prev)

let private mkfile (param : RPutParam) (total : Int64) (ctxs : String seq) =
    async {
        let customUri (key : String, value : String) =
            String.Format("/{0}/{1}", key, value)
        let mime = param.extra.mimeType
        let url = 
            [|
                param.c.config.upHost
                "/mkfile/" + total.ToString()
                "/key/" + Base64Safe.fromString param.key
                (if nullOrEmpty mime then "" else "/mimeType/" + Base64Safe.fromString mime)
                param.extra.customs |> Array.map customUri |> String.Concat
            |] |> concat
        let req = requestUrl url
        req.Method <- "POST"
        req.ContentType <- "text/plain"
        req.Headers.Add(HttpRequestHeader.Authorization, "UpToken " + param.token)
        use! output = requestStream req
        let body = ctxs |> interpolate "," |> concatUtf8
        do! output.AsyncWrite(body, 0, body.Length)
        return req
    } |!> responseJson<PutSucc>

let private doRput (param : RPutParam) (input : Stream) =
    let extra = param.extra
    let blockSize = extra.blockSize
    let blockCount = int32 ((input.Length + int64 blockSize - 1L) / int64 blockSize)
    let blockSizeOfId (blockId : Int32) =
        if blockId < blockCount - 1 then blockSize 
        else int32 (input.Length - int64 blockSize * int64 (blockCount - 1))

    let progresses = extra.progresses |> cleanProgresses
    let readAt = readerAt input

    let notifyLock = new Object()
    let notify (p : Progress) =
        lock notifyLock (fun _ -> extra.notify p)
    
    let work (blockId : Int32) =
        async {
            let blockCtx (prev : ChunkSucc) = { 
                blockId = blockId; blockSize = blockSizeOfId blockId;
                prev = prev; readAt = readAt; notify = notify 
            }
            let progress = progresses |> Array.tryFind (fun p -> p.blockId = blockId) 
            match progress with
            | Some p -> return! blockCtx p.ret |> block param
            | None -> return! blockCtx chunkSucc |> block param
        }

    async {
        let! rets = [| 0 .. blockCount - 1 |]
                    |> Array.map work
                    |> limitedParallel extra.worker
        if Array.forall checkRet rets then
            let ctxs = Array.map (fun (ret) -> (pickRet ret).ctx) rets
            return! mkfile param input.Length ctxs
        else return Error { error = "Block not all done" }
    }

let rput (c : Client) (token : String) (key : String) (input : Stream) (extra : RPutExtra) =
    async { return! doRput { c = c; token = token; key = key; extra = extra } input }

let rputFile (c : Client) (token : String) (key : String) (path : String) (extra : RPutExtra) =
    async {
        use input = File.OpenRead(path)
        return! rput c token key input extra
    }
