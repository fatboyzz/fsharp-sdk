namespace QiniuFSTest

open System
open System.IO
open System.Net
open System.Threading
open Newtonsoft.Json
open NUnit.Framework
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client
open TestBase

[<TestFixture>]
type IOTest() =

    member private this.PutWithExtraAsync (extra : IO.PutExtra) =
        async {
            let key = String.Format("{0}_{1}", ticks(), smallName)
            let en = entry tc.BUCKET key
            let token = uptoken key
            let! ret = IO.putFile c token key smallPath extra 
            match ret with
            | IO.PutSucc succ -> 
                do! RS.delete c en |>> check
                Assert.AreEqual(smallQETag, succ.hash)
            | IO.PutError e -> failwith e.error
        } 

    [<Test>]
    member this.PutTest() =
        this.PutWithExtraAsync IO.putExtra 
        |> Async.RunSynchronously

    [<Test>]
    member this.PutCrcTest() =
        this.PutWithExtraAsync { IO.putExtra with checkCrc = IO.CheckCrc.Auto }
        |> Async.RunSynchronously

    [<Test>]
    member this.RPutTest() =
        async {
            let key = String.Format("{0}_{1}", ticks(), bigName)
            let en = entry tc.BUCKET key
            let token = uptoken key
            let! ret = RIO.rputFile c token key bigPath RIO.rputExtra
            match ret with
            | IO.PutSucc succ -> 
                do! RS.delete c en |>> check
                Assert.AreEqual(bigQETag, succ.hash)
            | IO.PutError e -> failwith e.error
        } |> Async.RunSynchronously

    [<Test>]
    member this.ResumebleRPutTest() =
        let progressesData = "rputProgresses.dat"
        let progressesPath = Path.Combine(testPath, progressesData)
        if File.Exists(progressesPath) then File.Delete(progressesPath)

        let key = String.Format("{0}_{1}", ticks(), bigName)
        let en = entry tc.BUCKET key
        let token = uptoken key
        let notifyCancelCount = 10

        let upload _ =    
            let progresses =
                if File.Exists progressesPath then
                    use data = File.OpenRead(progressesPath)
                    readJsons<RIO.Progress> data 
                    |> Seq.toArray 
                    |> RIO.cleanProgresses
                else Array.zeroCreate<RIO.Progress> 0
            
            use data = File.Create(progressesPath)
            writeJsons data progresses

            let cs = new CancellationTokenSource()
            
            let notifyCount = ref -1
            let notify (p : RIO.Progress) =  
                incr notifyCount
                writeJson data p
                if unref notifyCount = notifyCancelCount then
                    cs.Cancel()
            
            let extra = {
                RIO.rputExtra with
                    RIO.progresses = progresses
                    RIO.notify = notify
            }

            use input = File.OpenRead bigPath
            let work = RIO.rput c token key input extra 
            try Async.RunSynchronously(work, -1, cs.Token)
            with | :? OperationCanceledException -> 
                IO.PutError({ error = "Upload not done yet and just try again"})
        
        let rec loop count =
            match upload() with
            | IO.PutSucc succ ->
                RS.delete c en |> checkSynchro
                Assert.AreEqual(bigQETag, succ.hash)
            | IO.PutError error ->
                loop (count + 1)

        loop 0  
        