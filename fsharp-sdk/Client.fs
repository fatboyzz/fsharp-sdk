module Qiniu.Client

open System
open System.IO
open System.Net
open System.Security.Cryptography
open Newtonsoft.Json
open Util

type Config = {
    accessKey : String
    secretKey : String

    rsHost : String
    rsfHost : String

    upHost : String
    ioHost : String
}


[<Struct>]
type Mac = 
    val AccessKey : String
    val SecretKey : byte[]

    new (c : Config) =
        { AccessKey = c.accessKey; SecretKey = stringToUtf8 c.secretKey }

    member private this.Compute(bs : byte[]) =
        use hmac = new HMACSHA1(this.SecretKey)
        hmac.ComputeHash(bs) |> Base64Safe.encode

    member this.Sign(bs : byte[]) =
        String.Format("{0}:{1}", this.AccessKey, this.Compute bs)

    member this.SignWithData(bs : byte[]) =
        let data = Base64Safe.encode bs
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
        String.Format("{0}:{1}", this.Bucket, this.Key) 

    member this.Encoded =
        this.Scope |> stringToBase64Safe

    override this.ToString() = this.Scope

let entry bucket key = new Entry(bucket, key)


let version = "1.0"
let userAgent =
    let osver = Environment.OSVersion.Version.ToString()
    String.Format("QiniuFSharp/{0} ({1};)", version, osver)

let config = {
    accessKey = "<Please apply your access key>"
    secretKey = "<Dont send your secret key to anyone>"

    rsHost = "http://rs.Qbox.me"
    rsfHost = "http://rsf.Qbox.me"

    upHost = "http://up.qiniu.com"
    ioHost = "http://iovip.qbox.me"
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

let accepted (code : HttpStatusCode) = int32 code / 100 = 2

let request (url : String) =
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

let responseJson (req : HttpWebRequest) =
    async {
        use! resp = responseCatched req
        let ok = accepted resp.StatusCode
        if resp.ContentType = "application/json"
        then 
            use stream = resp.GetResponseStream()
            use reader = new StreamReader(stream)
            let json = reader.ReadToEnd()
            return ok, json
        else 
            return ok, "{ \"error\" : \"Response ContentType is not application/json\"}"
    }

let parse (wrapSucc : 'a -> 'c) (wrapError : 'b -> 'c) (ok : bool, json : String) =
    if ok then json |> jsonToObject<'a> |> wrapSucc
    else json |> jsonToObject<'b> |> wrapError
