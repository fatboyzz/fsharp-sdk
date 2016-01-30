﻿namespace QiniuTest

open System
open System.IO
open System.Net
open System.Threading
open Newtonsoft.Json
open NUnit.Framework
open Qiniu
open Qiniu.Util
open Qiniu.Client
open TestBase

[<TestFixture>]
type IOTest() =
    let smallName = "small.dat"
    let smallPath = Path.Combine(testPath, smallName)
    do genNotExist smallPath (1 <<< 20) // 1M
    let smallMD5 = using (File.OpenRead smallPath) md5

    let bigName = "big.dat"
    let bigPath = Path.Combine(testPath, bigName)
    do genNotExist bigPath (1 <<< 23) // 8M
    let bigMD5 = using (File.OpenRead(bigPath)) md5

    member this.Token(key : String) =
        let policy = { 
            IO.putPolicy with
                scope = IO.scope <| entry tc.BUCKET key
                deadline = IO.defaultDeadline()
        }
        IO.sign c policy

    member this.GetMD5AndDelete(key : String) =
        let url = IO.publicUrl tc.DOMAIN key
        let req = WebRequest.Create url :?> HttpWebRequest
        let resp = req.GetResponse()
        let respStream = resp.GetResponseStream()
        let hash = resp.GetResponseStream() |> md5
        RS.delete c (entry tc.BUCKET key) |> checkSynchro
        hash

    [<Test>]
    member this.PutTest() =
        let key = String.Format("{0}_{1}", ticks(), smallName)
        let token = this.Token key
        use stream = File.OpenRead smallPath
        IO.put c token key stream IO.putExtra |> checkSynchro
        Assert.AreEqual(smallMD5, this.GetMD5AndDelete key)

    [<Test>]
    member this.PutCrcTest() =
        let key = String.Format("{0}_{1}", ticks(), smallName)
        let token = this.Token key
        use stream = File.OpenRead smallPath
        let extra = { IO.putExtra with checkCrc = IO.CheckCrc.Auto }
        IO.put c token key stream extra |> checkSynchro
        Assert.AreEqual(smallMD5, this.GetMD5AndDelete key)

    [<Test>]
    member this.RPutTest() =
        let key = String.Format("{0}_{1}", ticks(), bigName)
        let token = this.Token key
        use stream = File.OpenRead bigPath
        RIO.rput c token key stream RIO.rputExtra |> checkSynchro
        Assert.AreEqual(bigMD5, this.GetMD5AndDelete key)

    [<Test>]
    member this.ResumebleRPutTest() =
        let progressesData = "progresses.dat"
        let progressesPath = Path.Combine(testPath, progressesData)
        if File.Exists(progressesPath) then
            File.Delete(progressesPath)

        let key = String.Format("{0}_{1}", ticks(), bigName)
        let token = this.Token key
        let notifyCancelCount = 10

        let upload _ =    
            let progresses =
                if File.Exists progressesPath then
                    use data = File.OpenRead(progressesPath)
                    readJsons<RIO.Progress> data |> Seq.toArray
                else Array.zeroCreate<RIO.Progress> 0
            
            use data = File.Create(progressesPath)
            writeJsons data progresses

            let cs = new CancellationTokenSource()
            
            let notifyCount = ref -1
            let notifyLock = new Object()
            let notify (p : RIO.Progress) =
                lock notifyLock (fun _ -> 
                    incr notifyCount
                    writeJson data p
                    if unref notifyCount = notifyCancelCount then
                        cs.Cancel())
            
            let extra = {
                RIO.rputExtra with
                    RIO.progresses = progresses
                    RIO.notify = notify
            }
            use stream = File.OpenRead bigPath
            let work = RIO.rput c token key stream extra 
            try Async.RunSynchronously(work, -1, cs.Token)
            with | :? OperationCanceledException -> 
                IO.PutError({ error = "Upload not done yet and just try again"})
        
        let rec loop count =
            match upload() with
            | IO.PutSucc succ ->
                Assert.AreEqual(bigMD5, this.GetMD5AndDelete key)
            | IO.PutError error ->
                loop (count + 1)

        loop 0  
        