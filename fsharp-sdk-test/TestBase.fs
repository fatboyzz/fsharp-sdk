module QiniuTest.TestBase

open System
open System.IO
open System.Security.Cryptography
open Qiniu
open Qiniu.Util
open Qiniu.Client

let TEST_CONFIG_FILE = "TestConfig.json"

type TestConfig = {
    ACCESS_KEY : String
    SECRET_KEY : String
    BUCKET : String
    DOMAIN : String
}

let tc = File.ReadAllText(TEST_CONFIG_FILE) |> jsonToObject<TestConfig>

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
    let chunk = 1 <<< 12
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

let checkCallRet (ret : RS.CallRet) =
    match ret with
    | RS.CallSucc -> ()
    | RS.CallError e -> failwith e.error

let checkStatRet (ret : RS.StatRet) =
    match ret with
    | RS.StatSucc _ -> ()
    | RS.StatError e -> failwith e.error

let checkFetchRet (ret : RS.FetchRet) =
    match ret with
    | RS.FetchSucc _ -> ()
    | RS.FetchError e -> failwith e.error

let checkOpRet (ret : RS.OpRet) =
    match ret with
    | RS.OpSucc | RS.OpStatSucc _ -> ()
    | RS.OpError e -> failwith e.error

let checkPutRet (ret : IO.PutRet) =
    match ret with
    | IO.PutSucc _ -> ()
    | IO.PutError e -> failwith e.error
