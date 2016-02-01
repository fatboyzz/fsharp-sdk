Qiniu F# SDK 源码、编译、测试
===

## 准备

	Visual Studio 2013 (需要安装 F# )

## 下载源码

	git clone https://github.com/fatboyzz/fsharp-sdk

## 目录结构

- 打开 QiniuFS.sln 。
- 只有 QiniuFS 和 QiniuFSTest 项目中有实际的源码文件，使用 .net framework 4.5 编译。 
  QiniuFS_20、QiniuFS_40 ... 项目只拥有链接文件，使用对应的 .net framework 编译。
- F# 对项目源码的顺序有要求，为了方便排序，项目文件名前面有序号。

## 编译
	
从 NuGet下载依赖的库 NewtonSoft.Json 和 NUnit 。
- 右键解决方案 -> 管理 NuGet 程序包 -> 还原。

编译
- 生成 -> 生成解决方案

## 测试

下载 Visual Studio 的 NUnit 扩展。
- 工具 -> 扩展和更新 -> NUnit3 Test Adapter。
- 测试 -> 窗口 -> 测试资源管理器

可以先卸载不感兴趣的测试项目，比如去掉 QiniuFSTest_20 QiniuFSTest_40
- 右键对应项目 -> 卸载项目

生成解决方案，之后测试资源管理器中多出测试项。
- 生成 -> 生成解决方案

设置环境变量
- 新建一个临时文件夹，比如 M:\qiniutest
- 设置环境变量 QINIU_TEST_PATH 为 M:\qiniutest

新建测试空间
- 登陆七牛管理页面，新建对象存储 bucket "qiniutest"。

填写配置文件
- 复制项目下的 TestConfigTemplate.json 到 M:\qiniutest 。并改名成 TestConfig.json
- 文本编辑器打开 TestConfig.json 设置 ACCESS_KEY，SECRET_KEY 和 "qiniutest" 的 DOMAIN。

运行测试，这会在 QINIU_TEST_PATH 中生成并上传下载一些文件，测试时间跟网络环境有关，
一个框架的测试大概 50 秒。如果有测试不通过请发 issue。
- 测试资源管理器 -> 全部运行
