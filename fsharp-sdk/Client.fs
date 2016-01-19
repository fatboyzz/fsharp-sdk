module Qiniu.Client

open System
open System.IO
open System.Net
open System.Security.Cryptography
open Newtonsoft.Json
open Util

type Config = {
    version : String
    userAgent : String

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
        writeBytes buf uri
        if body <> null then writeBytes buf body
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


let private version = "1.0"
let private userAgent =
    let osver = Environment.OSVersion.Version.ToString()
    String.Format("QiniuFSharp/{0} ({1};)", version, osver)

let config = {
    version = version
    userAgent = userAgent

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

let accepted (status : Int32) = status / 100 = 2

let request (c : Client) (url : String) =
    let req = WebRequest.Create(url) :?> HttpWebRequest
    req.UserAgent <- c.config.userAgent
    req

let response (req : HttpWebRequest) =
    let resp = (try req.GetResponse() 
                with | :? WebException as e -> e.Response) :?> HttpWebResponse
    let ok = int32 resp.StatusCode |> accepted
    let json = if resp.ContentType = "application/json"
               then resp.GetResponseStream() |> streamToString 
               else ""
    ok, json

let parse (wrapSucc : 'a -> 'c) (wrapError : 'b -> 'c) (ok : bool, json : String) =
    if ok then json |> jsonToObject<'a> |> wrapSucc
    else json |> jsonToObject<'b> |> wrapError