namespace QiniuTest

open System
open System.IO
open System.Net
open NUnit.Framework
open Qiniu
open Qiniu.Client
open TestBase

[<TestFixture>]
type IOTest() =
    let smallName = "small.dat"
    do genNotExist smallName (1 <<< 20) // 1M
    let smallMD5 = using (File.OpenRead smallName) md5

    let bigName = "big.dat"
    do genNotExist bigName (1 <<< 23) // 8M
    let bigMD5 = using (File.OpenRead(bigName)) md5

    member this.TokenAndKey(name : String) =
        let key = String.Format("{0}_{1}", ticks(), name)
        let policy = { 
            IO.putPolicy with
                scope = IO.scope <| entry tc.BUCKET key
                deadline = IO.defaultDeadline()
        }
        IO.sign c policy, key

    member this.GetMD5AndDelete(key : String) =
        let url = IO.publicUrl tc.DOMAIN key
        let req = WebRequest.Create url :?> HttpWebRequest
        let resp = req.GetResponse()
        let respStream = resp.GetResponseStream()
        let hash = resp.GetResponseStream() |> md5
        RS.delete c (entry tc.BUCKET key) |> checkCallRet
        hash

    [<Test>]
    member this.PutTest() =
        let token, key = this.TokenAndKey smallName
        use stream = File.OpenRead smallName
        IO.put c token key stream IO.putExtra |> checkPutRet
        Assert.AreEqual(smallMD5, this.GetMD5AndDelete key)

    [<Test>]
    member this.RPutTest() =
        let token, key = this.TokenAndKey bigName
        use stream = File.OpenRead bigName
        RIO.rput c token key stream RIO.rputExtra |> checkPutRet
        Assert.AreEqual(bigMD5, this.GetMD5AndDelete key)