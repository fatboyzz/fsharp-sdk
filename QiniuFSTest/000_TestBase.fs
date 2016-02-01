﻿module QiniuFSTest.TestBase

open System
open System.IO
open System.Net
open System.Security.Cryptography
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client

let testPath = Environment.GetEnvironmentVariable("QINIU_TEST_PATH")
let testConfig = "TestConfig.json"

type TestConfig = {
    ACCESS_KEY : String
    SECRET_KEY : String
    BUCKET : String
    DOMAIN : String
}

let tc = 
    File.ReadAllText(Path.Combine(testPath, testConfig)) 
    |> jsonToObject<TestConfig>

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

let md5 (input : Stream) =
    use md5 = MD5.Create()
    let ret = md5.ComputeHash(input)
    ret

let md5File (path : String) =
    use input = File.OpenRead(path)
    md5 input

let smallName = "small.dat"
let smallPath = Path.Combine(testPath, smallName)
do genNotExist smallPath (1 <<< 20) // 1M
let smallMD5 = using (File.OpenRead smallPath) md5

let bigName = "big.dat"
let bigPath = Path.Combine(testPath, bigName)
do genNotExist bigPath (1 <<< 23) // 8M
let bigMD5 = using (File.OpenRead(bigPath)) md5

let check(o : Object) =
    match o with
    | :? RS.CallRet as ret -> RS.checkCallRet ret
    | :? RS.StatRet as ret -> RS.checkStatRet ret
    | :? RS.FetchRet as ret -> RS.checkFetchRet ret
    | :? RS.OpRet as ret -> RS.checkOpRet ret
    | :? IO.PutRet as ret -> IO.checkPutRet ret
    | :? D.DownRet as ret -> D.checkDownRet ret
    | _ -> failwith "unknown ret type"
        
let synchro = Async.RunSynchronously
    
let checkSynchro (ret : Async<'a>) =
    ret |>> box |>> check |> synchro

let uptoken (key : String) =
    let policy = { 
        IO.putPolicy with
            scope = IO.scope <| entry tc.BUCKET key
            deadline = IO.defaultDeadline()
    }
    IO.sign c policy

let putString (c : Client) (key : String, s : String) =
    let extra = { IO.putExtra with mimeType = "text/plain" }
    IO.put c (uptoken key) key (stringToStream s) extra

let getString (c : Client) (key : String) =
    let url = IO.publicUrl tc.DOMAIN key  
    let req = WebRequest.Create url :?> HttpWebRequest
    let resp = req.GetResponse()
    resp.GetResponseStream() 
    |> streamToString

