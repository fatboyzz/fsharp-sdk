[<AutoOpen>]
module QiniuFS.Client

open System
open System.IO
open System.Net
open System.Security.Cryptography
open Newtonsoft.Json

type Config = {
    accessKey : String
    secretKey : String

    rsHost : String
    rsfHost : String
    
    apiHost : String

    ioHost : String
    upHost : String
}

[<Struct>]
type Mac = 
    val AccessKey : String
    val SecretKey : byte[]

    new (c : Config) =
        { AccessKey = c.accessKey; SecretKey = stringToUtf8 c.secretKey }

    member private this.Compute(bs : byte[]) =
        use hmac = new HMACSHA1(this.SecretKey)
        hmac.ComputeHash(bs) |> Base64Safe.fromBytes

    member this.Sign(bs : byte[]) =
        String.Format("{0}:{1}", this.AccessKey, this.Compute bs)

    member this.SignWithData(bs : byte[]) =
        let data = Base64Safe.fromBytes bs
        let sign = data |> stringToUtf8 |> this.Compute
        String.Format("{0}:{1}:{2}", this.AccessKey, sign, data)

    member this.SignWithObject<'a>(obj : 'a) =
        obj |> objectToJson |> stringToUtf8 |> this.SignWithData

    member this.SignRequest(req : HttpWebRequest, body : byte[]) =
        use buf = new MemoryStream()
        let uri = req.Address.PathAndQuery + "\n" |> stringToUtf8
        buf.Write(uri, 0, uri.Length)
        if body <> null then buf.Write(body, 0, body.Length)
        let sign = buf.ToArray() |> this.Compute
        String.Format("{0}:{1}", this.AccessKey, sign)

[<Struct>]
type Entry = 
    val Bucket : String
    val Key : String

    new (bucket : String, key : String) =
        { Bucket = bucket; Key = key; }

    member this.Scope = 
        if notNullOrEmpty this.Key 
        then String.Format("{0}:{1}", this.Bucket, this.Key)
        else String.Format("{0}", this.Bucket)

    member this.Encoded =
        this.Scope |> Base64Safe.fromString

    override this.ToString() = this.Scope


let entry bucket key = new Entry(bucket, key)

let version = "1.0"
let userAgent =
    let osver = Environment.OSVersion.Version.ToString()
    String.Format("QiniuFSharp/{0} ({1};)", version, osver)

let config = {
    accessKey = "<Please apply your access key>"
    secretKey = "<Dont send your secret key to anyone>"

    rsHost = "http://rs.qbox.me"
    rsfHost = "http://rsf.qbox.me"

    apiHost = "http://api.qiniu.com"

    ioHost = "http://iovip.qbox.me"
    upHost = "http://up.qiniu.com"
}

type Client = {
    config : Config
    mac : Mac
}

let client (config : Config) =
    { config = config; mac = new Mac(config) }


type Error = {
    error : String
}

type Ret<'a> =
| Succ of 'a
| Error of Error

let mapRet (f : 'a -> 'b) (ret : Ret<'a>) =
    match ret with
    | Succ data -> Succ (f data)
    | Error e -> Error e

let pickRet (ret : Ret<'a>) =
    match ret with
    | Succ data -> data
    | Error e -> failwith e.error

let ignoreRet (ret : Ret<'a>) = 
    ret |> pickRet |> ignore

let checkRet (ret : Ret<'a>) =
    match ret with
    | Succ _ -> true
    | Error _ -> false

let accepted (code : HttpStatusCode) = int32 code / 100 = 2

let internal authorization (c : Client) (req : HttpWebRequest, body : byte[]) =
    String.Format("QBox {0}", c.mac.SignRequest(req, body))

let inline requestUrl (url : String) =
    let req = WebRequest.Create(url) :?> HttpWebRequest
    req.UserAgent <- userAgent
    req

let requestStream (req : HttpWebRequest) =
    Async.FromBeginEnd(req.BeginGetRequestStream, req.EndGetRequestStream)

let responseCatched (req : HttpWebRequest) =
    async {
        try 
            let! resp = req.AsyncGetResponse()
            return resp :?> HttpWebResponse
        with | :? WebException as e -> 
            return e.Response :?> HttpWebResponse
    }

let responseCopy (buf : byte[]) (req : HttpWebRequest) (output : Stream) =
    async {
        use! resp = responseCatched req
        use input = resp.GetResponseStream()
        do! asyncCopy buf input output
        return resp.StatusCode
    }

let responseStream (buf : byte[]) (req : HttpWebRequest) =
    async {
        let data = new MemoryStream()
        let! code = responseCopy buf req data
        data.Position <- 0L
        match accepted code with
        | true -> return Succ data
        | false -> return streamToString data |> jsonToObject<Error> |> Error
    }

let responseJson<'succ> (req : HttpWebRequest) =
    async {
        let jsonCapacity = 1 <<< 8 // 256B
        let buf = Array.zeroCreate jsonCapacity
        let! ret = responseStream buf req
        return mapRet (streamToString >> jsonToObject<'succ>) ret 
    }
    