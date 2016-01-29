module Qiniu.Util

open System
open System.IO
open System.Text
open System.Net
open System.Threading
open Newtonsoft.Json

let unref (r : 'a ref) = !r

let stringToUtf8 (s : String) =
    Encoding.UTF8.GetBytes(s)

let stringToBase64Safe (s : String) =
    s |> stringToUtf8 |> Base64Safe.encode

let utf8ToString (bs : byte[]) =
    Encoding.UTF8.GetString(bs)

let base64SafeToString (s : String) =
    s |> Base64Safe.decode |> utf8ToString

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

let concat (ss : String seq) = 
    String.Concat ss

let join (sep : String) (ss : String seq) =
    String.Join(sep, Seq.toArray ss)

let crlf = "\r\n"

let nullOrEmpty = String.IsNullOrEmpty

let headers (req : HttpWebRequest) =
    let hs = req.Headers
    hs.AllKeys |> Array.map (fun (key) -> (key, hs.[key]))

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

type FSharpType = Microsoft.FSharp.Reflection.FSharpType
type FSharpValue = Microsoft.FSharp.Reflection.FSharpValue

let isClass (t : Type) = t.IsClass
let isValue (t : Type) = t.IsValueType
let isRecord = FSharpType.IsRecord
let isTuple = FSharpType.IsTuple
let isArray (t : Type) = t.IsArray

let rec private zeroInstance (t : Type) =
    match t with
    | _ when isValue t -> Activator.CreateInstance t
    | _ when isRecord t -> zeroRecord t
    | _ when isTuple t -> zeroTuple t
    | _ when isArray t -> zeroArray t
    | _ -> null

and private zeroRecord (t : Type) =
    FSharpType.GetRecordFields t
    |> Array.map (fun p -> zeroInstance p.PropertyType)
    |> (fun objs -> FSharpValue.MakeRecord (t, objs))

and private zeroTuple (t : Type) =
    FSharpType.GetTupleElements t
    |> Array.map zeroInstance
    |> (fun objs -> FSharpValue.MakeTuple (objs, t))

and private zeroArray (t : Type) =
    Array.CreateInstance(t.GetElementType(), 0) :> Object

let zero<'a> : 'a = zeroInstance typeof<'a> :?> 'a
    