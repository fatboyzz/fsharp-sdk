Qiniu F# SDK 使用指南
===

## 通用部分

- 所有例子均打开了以下空间和模块
	open Qiniu
	open Qiniu.Client

- 大部分 api 是异步调用。使用了 F# 异步流。

- 大部分 api 第一个参数是 Client 。用以下代码创建一个 Client。
注意以后的 api 如果需要均使用 c 作为第一个参数。
	let c = client {
	    config with
	        accessKey = "<Please apply your access key>"
	        secretKey = "<Dont send your secret key to anyone>"
	}

- 大部分 api 异步操作完成之后的返回类型是 ***Ret，比如 PutRet、StatRet。这是一个 Union 。
模式匹配取出返回值并分别处理或者简单的使用 check***Ret 作处理。下面代码定义了 checkPutRet。
	let checkPutRet (ret : PutRet) =
	    match ret with
	    | PutSucc _ -> ()
	    | PutError e -> failwith e.error

- 使用 F# 模块划分了功能。具体功能的标题中会有说明。

## 上传策略 (模块 IO)

- 创建 IO.PutPolicy
	let policy = { 
        IO.putPolicy with
            scope = IO.scope <| entry tc.BUCKET key
            deadline = IO.defaultDeadline()
    }

- 制作 uptoken
	let uptoken = IO.sign c policy


## 小文件上传 (4M以下，模块 IO)

- 服务器端生成一个 uptoken 。参见上传策略。
- 服务器通过网络把 uptoken 传给 客户端
- 客户端上传
	IO.put c uptoken key stream IO.putExtra 
	|> Async.RunSynchronously |> checkPutRet

## 大文件上传 (分块，并行，断点，模块 IO、RIO)

- 服务器端生成一个 uptoken 。参见上传策略。
- 服务器通过网络把 uptoken 传给 客户端
- 客户端上传
	RIO.rput c uptoken key stream RIO.rputExtra
	|> Async.RunSynchronously |> checkRPutRet

## 获取下载 url (模块 IO)

- 公有 url，客户端可以直接生成
	let url = IO.publicUrl domain key

- 私有 url，服务器生成之后传给客户端
	let url = IO.privateUrl c domain key deadline

## 小文件下载 (4M以下，模块 IO、D)

- 获取 url。参见获取下载 url
- 客户端下载
	D.down url D.downExtra path
	|> Async.RunSynchronously |> checkDownRet

## 大文件下载 (分块，并行，断点，模块 IO、RD)

- 获取 url。参见获取下载 url
- 客户端下载
	RD.rdown url RD.rdownExtra path
	|> Async.RunSynchronously |> checkRDownRet	

## 资源管理 (模块 RS、RSF)

- 指定文件的位置，后面代码中的 en，src，dst 都是文件位置。
	let en = entry bucket key

- 查看、删除、复制、移动。
	RS.stat c en
	RS.delete c en
	RS.copy c src dst
	RS.move c src dst

- 批量操作
	RS.batch c [| 
		OpStat(en)
		OpDelete(en)
		OpCopy(src, dst)
		OpMove(src, dst)
	|]

- 抓取
	RS.fetch c url dst

- 修改 Mime
	RS.changeMime c mime en
