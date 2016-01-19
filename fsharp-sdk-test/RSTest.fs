namespace QiniuTest

open System
open System.IO
open System.Net
open NUnit.Framework
open Qiniu
open Qiniu.Util
open Qiniu.Client
open TestBase

[<TestFixture>]
type RSTest() =
    member this.PutString(key : String, s : String) =
        let e = entry tc.BUCKET key
        let policy = { 
            IO.putPolicy with
                scope =  e.Scope
                deadline = IO.defaultDeadline()
        }
        let token = IO.sign c policy
        let extra = { IO.putExtra with mimeType = "text/plain" }
        IO.put c token key (stringToStream s) extra |> checkPutRet

    member this.DownString(key : String) =
        let url = IO.publicUrl tc.DOMAIN key  
        let req = WebRequest.Create url :?> HttpWebRequest
        let resp = req.GetResponse()
        resp.GetResponseStream() |> streamToString

    [<Test>]
    member this.StatTest() =
        this.PutString("stat.txt", "statstat")
        let e = entry tc.BUCKET "stat.txt"
        RS.stat c e |> checkStatRet
        RS.delete c e |> checkCallRet

    [<Test>]
    member this.DeleteTest() =
        this.PutString("deleteMe.txt", "deleteMe")
        RS.delete c (entry tc.BUCKET "deleteMe.txt") |> checkCallRet

    [<Test>]
    member this.CopyTest() =
        let content = "orig"
        this.PutString("copySrc.txt", content)
        let src = entry tc.BUCKET "copySrc.txt"
        let dst = entry tc.BUCKET "copyDst.txt"
        RS.copy c src dst |> checkCallRet
        let ret = this.DownString "copyDst.txt"
        Assert.AreEqual(content, ret)
        RS.delete c src |> checkCallRet
        RS.delete c dst |> checkCallRet

    [<Test>]
    member this.MoveTest() =
        this.PutString("beforeMove.txt", "content")
        let src = entry tc.BUCKET "beforeMove.txt"
        let dst = entry tc.BUCKET "afterMove.txt"
        RS.move c src dst |> checkCallRet
        RS.delete c dst |> checkCallRet  

    
    