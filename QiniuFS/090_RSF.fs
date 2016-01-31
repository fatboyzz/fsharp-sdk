module Qiniu.RSF

open System
open Qiniu.Util
open Qiniu.Client
open Qiniu.RS

type ListItem = {
    key : String
    hash : String
    fsize : Int64
    putTime : Int64
    mimeType : String
    endUser : String
}

type ListSucc = {
    marker : String
    commonPrefixes : String[]
    items : ListItem[]
}

type ListRet = | ListSucc of ListSucc | ListError of Error

let private parseListRet = parse ListSucc ListError

let list (c : Client) (bucket : String) (limit : Int32) (prefix : String) (delimiter : String) (marker : String) =
    let query = 
        seq { 
            yield String.Format("bucket={0}", bucket)
            if limit > 0 then yield String.Format("limit={0}", limit)
            if nullOrEmpty prefix |> not then yield String.Format("prefix={0}", prefix)
            if nullOrEmpty delimiter |> not then yield String.Format("delimiter={0}", delimiter)
            if nullOrEmpty marker |> not then yield String.Format("marker={0}", marker)
        } |> Seq.toArray |> join "&"
    String.Format("{0}/list?{1}", c.config.rsfHost, query)
    |> requestOp c |> responseJson |>> parseListRet
    