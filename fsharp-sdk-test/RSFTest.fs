namespace QiniuTest

open System
open NUnit.Framework
open Qiniu
open Qiniu.Util
open Qiniu.Client
open TestBase

[<TestFixture>]
type RSFTest() =
    
    let listLength = 20
    let listLimit = 5

    [<SetUp>]
    member this.SetUp() =
        [| 0 .. listLength - 1 |]
        |> Array.map (fun id -> 
            let s = id.ToString()
            putString c (s, s))
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.map check
        |> ignore

    [<TearDown>]
    member this.TearDown() =
        [| 0 .. listLength - 1 |]
        |> Array.map (fun id -> 
            let e = entry tc.BUCKET (id.ToString())
            RS.delete c e)
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.map check
        |> ignore

    [<Test>]
    member this.ListTest() =
        let rec loop marker acc =
            async {
                let! ret = RSF.list c tc.BUCKET listLimit "" "" marker
                match ret with
                | RSF.ListSucc succ ->
                    let nextAcc = acc + succ.items.Length
                    if nullOrEmpty succ.marker then return nextAcc
                    else return! loop succ.marker nextAcc
                | RSF.ListError error -> 
                    return failwith error.error
            }
        let acc = loop "" 0 |> Async.RunSynchronously
        Assert.AreEqual(listLength, acc)