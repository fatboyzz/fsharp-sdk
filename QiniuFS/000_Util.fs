module QiniuFS.Util

open System
open System.IO
open System.Text
open System.Net
open System.Threading
open Newtonsoft.Json

let unref (r : 'a ref) = !r

let stringToUtf8 (s : String) =
    Encoding.UTF8.GetBytes(s)

let utf8ToString (bs : byte[]) =
    Encoding.UTF8.GetString(bs)

let streamToString (stream : Stream) =
    let reader = new StreamReader(stream, Encoding.UTF8)
    reader.ReadToEnd()

let stringToStream (s : String) =
    new MemoryStream(stringToUtf8 s)

let jsonSettings =
    new JsonSerializerSettings(
        NullValueHandling = NullValueHandling.Ignore
    )

let jsonToObject<'a>(s : String) : 'a =
    JsonConvert.DeserializeObject<'a>(s, jsonSettings)

let objectToJson<'a>(o : 'a) =
    JsonConvert.SerializeObject(o, jsonSettings)

let objectToJsonIndented<'a>(o : 'a) =
    JsonConvert.SerializeObject(o, Formatting.Indented, jsonSettings)

let readJsons<'a>(input : Stream) =
    seq {
        let r = new StreamReader(input, Encoding.UTF8)
        let jr = new JsonTextReader(r)
        jr.SupportMultipleContent <- true
        let js = JsonSerializer.Create(jsonSettings)
        while jr.Read() do
            yield js.Deserialize<'a>(jr)
    }

let writeJson (output : Stream) (o : 'a) =
    let w = new StreamWriter(output, Encoding.UTF8)
    let jw = new JsonTextWriter(w)
    let js = JsonSerializer.Create(jsonSettings)
    js.Serialize(jw, o)
    jw.Flush()

let writeJsons (output : Stream) (os : 'a seq) = 
    let w = new StreamWriter(output, Encoding.UTF8)
    let jw = new JsonTextWriter(w)
    let js = JsonSerializer.Create(jsonSettings)
    for o in os do
        js.Serialize(jw, o)
    jw.Flush()

let concat (ss : String[]) = 
    String.Concat(ss)

let join (sep : String) (ss : String[]) =
    String.Join(sep, ss)

let concatBytes (bss : byte[][]) =
    let c = bss |> Array.map Array.length |> Array.sum
    let s = new MemoryStream(c)
    for bs in bss do
        s.Write(bs, 0, bs.Length)
    s.ToArray()

let crlf = "\r\n"

let nullOrEmpty = String.IsNullOrEmpty

let readerAt (input : Stream) =
    let inputLock = new Object()
    fun (offset : Int64) (length : Int32) ->
        let buf : byte[] = Array.zeroCreate length
        lock inputLock (fun _ ->
            input.Position <- offset
            input.Read(buf, 0, length) |> ignore
            buf
        )

let writerAt (output : Stream) =
    let outputLock = new Object()
    fun (offset : Int64) (data : byte[]) ->
        lock outputLock (fun _ -> 
            output.Position <- offset
            output.Write(data, 0, data.Length)
        )

let (|>>) (computaion : Async<'a>) (con : 'a -> 'b) =
    async.Bind(computaion, con >> async.Return)

let (|!>) (computaion : Async<'a>) (con : 'a -> Async<'b>) =
    async.Bind(computaion, con >> async.ReturnFrom)

let asyncCopy (buf : byte[]) (src : Stream) (dst : Stream) =
    let length = buf.Length
    let rec loop _ =
        async {
            let! n = src.AsyncRead(buf, 0, length)
            if n <> 0 then 
                do! dst.AsyncWrite(buf, 0, n)
                return! loop()
        }
    loop()

let limitedParallel (limit : Int32) (jobs : Async<'a>[]) =
    async {
        let length = jobs.Length
        let count = ref -1
        let rets : 'a[] = Array.zeroCreate length
        let rec worker wid =
            async {
                let index = Interlocked.Increment count
                if index < length then
                    let! ret = jobs.[index]
                    rets.[index] <- ret
                    do! worker wid
            }
        do! Array.init limit worker 
            |> Async.Parallel 
            |> Async.Ignore
        return rets
    }
    