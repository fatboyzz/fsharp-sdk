namespace QiniuFSTest

open System
open System.IO
open System.Net
open System.Threading
open Newtonsoft.Json
open NUnit.Framework
open QiniuFS
open Base

[<TestFixture>]
type IOTest() =

    member private this.PutWithExtraAsync (extra : IO.PutExtra) =
        let key = String.Format("{0}_{1}", ticks(), smallName)
        let en = entry tc.BUCKET key
        let token = uptoken key
        let ret = IO.putFile c token key smallPath extra |> pickRetSynchro
        RS.delete c en |> ignoreRetSynchro
        Assert.AreEqual(smallQETag, ret.hash)

    [<Test>]
    member this.PutTest() =
        this.PutWithExtraAsync IO.putExtra 

    [<Test>]
    member this.PutCrcTest() =
        this.PutWithExtraAsync { IO.putExtra with checkCrc = IO.CheckCrc.Auto }

    [<Test>]
    member this.RPutTest() =
        let key = String.Format("{0}_{1}", ticks(), bigName)
        let en = entry tc.BUCKET key
        let token = uptoken key
        let ret = RIO.rputFile c token key bigPath RIO.rputExtra |> pickRetSynchro
        RS.delete c en |> ignoreRetSynchro
        Assert.AreEqual(bigQETag, ret.hash)


    member this.RPutProgresses(progressesPath : String) =
        if File.Exists progressesPath then
            use data = File.OpenRead(progressesPath)
            readJsons<RIO.Progress> data 
            |> Seq.toArray 
            |> RIO.cleanProgresses
        else Array.zeroCreate<RIO.Progress> 0

    member this.RPutExtra(progresses : RIO.Progress[], notify : RIO.Progress -> unit) = 
        { RIO.rputExtra with
            RIO.progresses = progresses
            RIO.notify = notify }

    member this.RPutCancel(cancelCount : Int32, progressesPath : String, key : String) =
        let progresses = this.RPutProgresses progressesPath
        use progressStream = File.Create(progressesPath)
        writeJsons progressStream progresses

        let cs = new CancellationTokenSource()
        let notifyCount = ref -1
        let notify (p : RIO.Progress) =  
            incr notifyCount
            writeJson progressStream p
            if unref notifyCount >= cancelCount then
                cs.Cancel()
            
        let extra = this.RPutExtra(progresses, notify)
        let work = RIO.rputFile c (uptoken key) key bigPath extra 
        try Async.RunSynchronously(work, -1, cs.Token)
        with | :? OperationCanceledException -> 
            Error { error = "Upload not done yet and just try again" }

    [<Test>]
    member this.RPutProgressTest() =
        let progressesData = "rputProgresses.dat"
        let progressesPath = Path.Combine(testPath, progressesData)
        if File.Exists(progressesPath) then File.Delete(progressesPath)

        let key = String.Format("{0}_{1}", ticks(), bigName)
        let en = entry tc.BUCKET key
        let cancelCount = 10
        
        let rec loop count =
            match this.RPutCancel(cancelCount, progressesPath, key) with
            | Succ succ ->
                RS.delete c en |> ignoreRetSynchro
                Assert.AreEqual(bigQETag, succ.hash)
            | Error error ->
                loop (count + 1)

        loop 0  
        