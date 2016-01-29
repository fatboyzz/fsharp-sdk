module QiniuTest.TestBase

open System
open System.IO
open System.Net
open System.Security.Cryptography
open Qiniu
open Qiniu.Util
open Qiniu.Client

let testPath = Environment.GetEnvironmentVariable("QINIU_TEST_PATH")
let testConfig = "TestConfig.json"

type TestConfig = {
    ACCESS_KEY : String
    SECRET_KEY : String
    BUCKET : String
    DOMAIN : String
}

let tc = File.ReadAllText(Path.Combine(testPath, testConfig)) |> jsonToObject<TestConfig>

let c = client {
    config with
        accessKey = tc.ACCESS_KEY
        secretKey = tc.SECRET_KEY
}

let r = new Random(int32 (DateTime.Now.Ticks &&& 0xFFFFL))

let ticks _ =
    DateTime.Now.Ticks.ToString()

let genFile (path : String) (size : Int32) =
    use file = File.Create(path)
    let chunk = 1 <<< 14 // 16K
    let count = (size + chunk - 1) / chunk
    let chunkLast = size - (count - 1) * chunk
    let buf = Array.zeroCreate(chunk)
    for i = 0 to count - 1 do
        r.NextBytes(buf)
        file.Write(buf, 0, if i < count - 1 then chunk else chunkLast)

let genNotExist (path : String) (size : Int32) =
    if File.Exists(path) |> not then
        genFile path size

let md5 (s : Stream) =
    use md5 = MD5.Create()
    let ret = md5.ComputeHash(s)
    ret

let check(o : Object) =
    match o with
    | :? RS.CallRet as ret -> RS.checkCallRet ret
    | :? RS.StatRet as ret -> RS.checkStatRet ret
    | :? RS.FetchRet as ret -> RS.checkFetchRet ret
    | :? RS.OpRet as ret -> RS.checkOpRet ret
    | :? IO.PutRet as ret -> IO.checkPutRet ret
    | _ -> failwith "unknown ret type"
        
let synchro = Async.RunSynchronously
    
let checkSynchro (ret : Async<'a>) =
    ret |>> box |>> check |> synchro

let putString (c : Client) (key : String, s : String) =
    let e = entry tc.BUCKET key
    let policy = { 
        IO.putPolicy with
            scope =  e.Scope
            deadline = IO.defaultDeadline()
    }
    let token = IO.sign c policy
    let extra = { IO.putExtra with mimeType = "text/plain" }
    IO.put c token key (stringToStream s) extra

let getString (c : Client) (key : String) =
    let url = IO.publicUrl tc.DOMAIN key  
    let req = WebRequest.Create url :?> HttpWebRequest
    let resp = req.GetResponse()
    resp.GetResponseStream() |> streamToString