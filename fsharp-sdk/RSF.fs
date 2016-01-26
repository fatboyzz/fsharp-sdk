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
        [| 
            String.Format("bucket={0}", bucket)
            (if limit = 0 then "" else String.Format("limit={0}", limit))
            (if nullOrEmpty prefix then "" else String.Format("limit={0}", limit))
            (if nullOrEmpty delimiter then "" else String.Format("delimiter={0}", delimiter))
            (if nullOrEmpty marker then "" else String.Format("marker={0}", marker))
        |] |> join "&"
    String.Format("{0}/list?{1}", c.config.rsfHost, query)
    |> requestOp c |> responseJson |>> parseListRet
