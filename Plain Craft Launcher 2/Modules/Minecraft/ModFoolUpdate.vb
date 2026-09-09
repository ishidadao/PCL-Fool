Imports System.Security.Cryptography

Public Module ModFoolUpdate

    Private Const FoolPackId As String = "the-fool"
    Private Const FoolMarkerName As String = ".fool-managed.json"
    Private Const ManagedDiscoveryPort As Integer = 4443
    Private Const ManagedDiscoveryPath As String = "/.well-known/pcl-managed.json"
    ' Keep the retired key during the transition so a new launcher can still verify
    ' discovery documents produced before the server-side signing key was rotated.
    Private ReadOnly FoolTrustedPublicKeysXml As String() = {
        "<RSAKeyValue><Modulus>xnnmUgsZw7Bs1OF9+UULn45GRqEzYybC9TT/ozlGSaxlvIUhxBENLM39FmPcEmGA69Ex9xEt9ENDCdZ1doL+Ao0vMXuVAq99aHEww4OzdNrdWsHnnUT/MJCqXS5HuobT4ZN42z1iOddDBM/I9okQUdbVAxBbk15l1vIw/TryAy6HGSaYGE741EvoOr5RQtzufyWlg5UwuylamjOqUDVuNurrN4kNUWGqUchIEsvqA7u2ArzJ23b7yyCz1CQ80LIA+JpHq0/TgXmPDT1maYjCiIqV9nPOB9sT2lM5Q1Z88JvNxJqdvDbyvDT6nhwaNEdfc3A/ke5/uZTgLSCPNtSSCWGA8Hq6mo4sIaOn5G3gqvXbgV27recuaFUmWN6Q5QjbwwIMrIGYZUUwKyJtuvU03Y0WpjarCT1lIEqw8uT6OqstyOu39PFDqWm81Ugpuj9JStAvnaPulM3lxPqc/8KsL0nLLitZUs+jo8SYJGUmyrsNLkDUdfggTzci34UwAJmD</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>",
        "<RSAKeyValue><Modulus>rtthlrMQPmTy/1vmqWsxQuDWWJWyph55l8cwCpvrlQYaoLJ2rCCdmbVXIhXE+CE9mex8lEJZs1ZQEhGFWj9D5yzUSS8f7CqmTqu5gfJv5/0AL7hW6yS4yj2cSiS/+THBSSwSrMoRZG7S4k5DzVoJLIwOvhLOJaN1jtC6D5LrdeaUyoTANwwl4qo6e4r4V2HzcLpp29+6R/knL0fmRsfzaMRfQGmyqvgjVQXjss18F8ti9oiW62xgA9PoKYpbUazFS7FAOguy5tY7Z9r3BMj7AoVLLrEZVmPRy/X7gLFLkzRGL6p4rsizYg81qdJbY0Man18XXJOnX6SCpPJ5unmM7jKV6b6HhSYd7LIpE68HEDlARYTVVY/45hI+9zJ7d2fDIctYC32l5paxrSf86fiUsNsqTg5kBm0zQlVN751AS8GlUXRDzfcz/bXf1rj/GsmsoUV12eyCK+RA8QmU3lyIKmFTfrWDxO/FBnCTthzTgSjXo8ZvAOff8IJZwMmlGm7r</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"
    }
    Private ReadOnly FoolAllowedRoots As String() = {"mods", "config", "defaultconfigs", "kubejs", "emotes", "resourcepacks", "shaderpacks"}

    Private Class FoolManagedFile
        Public RelativePath As String
        Public TargetPath As String
        Public DownloadUrl As String
        Public Sha256 As String
        Public Size As Long
        Public StagePath As String
    End Class

    ''' <summary>
    ''' Discovers a trusted update service from the player-entered server address and,
    ''' when a valid signed Fool manifest is advertised, synchronizes before connecting.
    ''' </summary>
    Public Sub FoolUpdateBeforeLaunch(Loader As LoaderTask(Of Integer, Integer))
        If McInstanceSelected Is Nothing OrElse CurrentLaunchOptions Is Nothing OrElse
           Not CurrentLaunchOptions.ManagedServerDiscoveryRequested OrElse Not CurrentLaunchOptions.ServerIp.Any Then Return

        Dim InstanceRoot = Path.GetFullPath(McInstanceSelected.PathIndie)
        Dim MarkerPath = Path.Combine(InstanceRoot, FoolMarkerName)
        Dim RequestedServer = CurrentLaunchOptions.ServerIp.Trim
        Dim ExistingMarker As JObject = Nothing
        Dim MarkerError As Exception = Nothing
        If FileUtils.Exists(MarkerPath) Then
            Try
                ExistingMarker = JObject.Parse(FileUtils.ReadAsString(MarkerPath))
            Catch ex As Exception
                MarkerError = ex
            End Try
        End If

        Dim DiscoveryUrl = BuildManagedDiscoveryUrl(RequestedServer)
        Dim DiscoveryText As String
        Try
            DiscoveryText = NetRequestByClient(DiscoveryUrl, Timeout:=5000, RequireJson:=True)
        Catch ex As Exception
            Throw New Exception("$已选择《此服务器提供自动更新》，但无法连接它的更新发现接口，已阻止启动。" & vbCrLf &
                $"发现地址：{DiscoveryUrl}" & vbCrLf &
                "请检查网络或联系服务器管理员；若该服务器本来就没有更新服务，请取消勾选后普通直连。", ex)
        End Try

        Dim DiscoveryPayloadBytes As Byte() = Nothing
        Dim Discovery As JObject
        Try
            Discovery = ParseFoolSignedEnvelope(DiscoveryText, DiscoveryPayloadBytes)
            If Discovery.Value(Of Integer?)("schema") <> 1 OrElse
               Discovery.Value(Of String)("kind") <> "pcl-managed-server" OrElse
               Discovery.Value(Of String)("packId") <> FoolPackId Then
                Throw New FormatException("发现文档身份或版本不正确")
            End If
        Catch ex As Exception
            Throw New Exception("$服务器的更新发现文档未通过数字签名或身份校验，已阻止启动。" & vbCrLf &
                $"发现地址：{DiscoveryUrl}", ex)
        End Try

        Dim ConnectServer = If(Discovery.Value(Of String)("serverAddress"), "").Trim
        Dim ConnectHost As String = Nothing, ConnectPort As Integer
        If Not TrySplitMinecraftServerAddress(ConnectServer, ConnectHost, ConnectPort) Then
            Throw New Exception("$服务器签名的联机地址格式不正确，已阻止启动。")
        End If
        Dim ManifestUrl = If(Discovery.Value(Of String)("manifestUrl"), "").Trim
        Dim ManifestUri As Uri = Nothing
        If Not Uri.TryCreate(ManifestUrl, UriKind.Absolute, ManifestUri) OrElse ManifestUri.Scheme <> Uri.UriSchemeHttps Then
            Throw New Exception("$服务器签名的更新清单地址不是有效的 HTTPS URL，已阻止启动。")
        End If

        If MarkerError IsNot Nothing Then
            Throw New Exception("$当前实例的愚者更新标记损坏，已阻止启动。请保留实例并联系服务器管理员。", MarkerError)
        End If
        If ExistingMarker IsNot Nothing AndAlso
           (ExistingMarker.Value(Of Integer?)("schema") <> 1 OrElse ExistingMarker.Value(Of String)("packId") <> FoolPackId) Then
            Throw New Exception("$当前实例的更新标记不属于服务器签名声明的整合包，已阻止启动。")
        End If

        Logger.Info($"[服务器发现] 已从玩家输入的 {RequestedServer} 获取并验证更新清单地址：{ManifestUrl}")
        Loader.Progress = 0.01
        Dim EnvelopeText As String
        Try
            EnvelopeText = NetRequestByClientRetry(ManifestUrl, RequireJson:=True)
        Catch ex As Exception
            Throw New Exception("$无法连接愚者更新服务器，已阻止启动。请检查网络后重试。", ex)
        End Try
        Loader.Progress = 0.04
        Dim PayloadBytes As Byte() = Nothing
        Dim Payload As JObject
        Try
            Payload = ParseFoolSignedEnvelope(EnvelopeText, PayloadBytes)
            If Payload.Value(Of Integer?)("schema") <> 1 OrElse Payload.Value(Of String)("packId") <> FoolPackId Then
                Throw New FormatException("清单身份或版本不正确")
            End If
            If Not ServerAddressesEqual(ConnectServer, Payload.Value(Of String)("serverAddress")) Then
                Throw New FormatException("清单声明的联机服务器与发现文档不一致")
            End If
        Catch ex As Exception
            Throw New Exception("$愚者更新清单未通过数字签名校验，已阻止启动。请联系服务器管理员。", ex)
        End Try
        Loader.Progress = 0.06

        If Not FoolInstanceIsIsolated(McInstanceSelected) Then
            Throw New Exception("$受管《愚者》实例不能与整个 .minecraft 共用游戏目录。" & vbCrLf &
                "请先在版本设置中启用版本隔离，避免同步器改动其他实例的 Mod 或配置。")
        End If
        ValidateFoolRuntime(Payload)

        If ExistingMarker Is Nothing OrElse ExistingMarker.Value(Of Boolean?)("confirmed") <> True Then
            Dim PackVersion = If(Payload.Value(Of String)("packVersion"), "未知")
            Dim Confirmed = RunInUiWait(Function() MyMsgBox(
                $"服务器 {RequestedServer} 提供了已通过内置公钥验证的《愚者》{PackVersion} 更新清单。" & vbCrLf & vbCrLf &
                "继续后，启动器会同步 mods、config、defaultconfigs、kubejs、emotes、resourcepacks、shaderpacks。" & vbCrLf &
                "被替换或移出的文件会先保存到 PCL/FoolUpdate/backups；存档、截图、账号与 options.txt 不会被修改。",
                "接入服务器签名同步", "确认接入", "取消", IsWarn:=True))
            If Confirmed <> 1 Then Throw New OperationCanceledException
        End If

        Try
            Dim Marker As New JObject From {
                {"schema", 1},
                {"packId", FoolPackId},
                {"channel", "stable"},
                {"confirmed", True},
                {"discoveryUrl", DiscoveryUrl},
                {"manifestUrl", ManifestUrl},
                {"discoveryServerAddress", RequestedServer},
                {"serverAddress", ConnectServer}
            }
            FileUtils.Write(MarkerPath, Marker.ToString(Formatting.Indented), encoding:=New UTF8Encoding(False))
        Catch ex As Exception
            Throw New Exception("$无法保存当前服务器的受管更新标记，已阻止启动。请检查实例目录写入权限。", ex)
        End Try
        CurrentLaunchOptions.ServerIp = ConnectServer
        Logger.Info($"[愚者更新] 同步完成后将连接服务器签名返回的地址：{ConnectServer}")

        Dim ManifestFiles = ParseFoolFiles(Payload, ManifestUrl, InstanceRoot)
        Dim ExplicitRemovals = ParseFoolRemovals(Payload, InstanceRoot)
        Dim PayloadSha256 = CryptographyUtils.ComputeHash(PayloadBytes, CryptographyUtils.HashMethod.Sha256)
        Dim StatePath = Path.Combine(InstanceRoot, "PCL", "FoolUpdate", "state.json")
        Dim PreviousState = ReadFoolState(StatePath)
        Dim PreviousPaths = ReadFoolManagedPaths(PreviousState)
        Dim ExplicitObsoletePaths As New List(Of String)
        For Each Entry In ExplicitRemovals
            If Not FileUtils.Exists(Entry.TargetPath) Then Continue For
            Dim ExistingSha256 = CryptographyUtils.ComputeFileHash(Entry.TargetPath, CryptographyUtils.HashMethod.Sha256)
            If Not String.Equals(ExistingSha256, Entry.Sha256, StringComparison.OrdinalIgnoreCase) Then
                Throw New Exception("$检测到需要迁移的旧文件已被修改，因此没有删除并已阻止启动：" & Entry.RelativePath & vbCrLf &
                    "请将 PCL 日志发给服务器管理员处理。")
            End If
            ExplicitObsoletePaths.Add(Entry.RelativePath)
        Next

        Dim Pending As New List(Of FoolManagedFile)
        For i = 0 To ManifestFiles.Count - 1
            If Loader.IsCanceled Then Throw New OperationCanceledException
            Dim Entry = ManifestFiles(i)
            Dim IsCorrect = FileUtils.Exists(Entry.TargetPath) AndAlso
                New FileInfo(Entry.TargetPath).Length = Entry.Size AndAlso
                String.Equals(CryptographyUtils.ComputeFileHash(Entry.TargetPath, CryptographyUtils.HashMethod.Sha256), Entry.Sha256, StringComparison.OrdinalIgnoreCase)
            If Not IsCorrect Then Pending.Add(Entry)
            Loader.Progress = 0.06 + 0.34 * (i + 1) / Math.Max(ManifestFiles.Count, 1)
        Next

        Dim WorkRoot = Path.Combine(InstanceRoot, "PCL", "FoolUpdate")
        Dim StageRoot = Path.Combine(WorkRoot, "stage", PayloadSha256)
        Directory.CreateDirectory(StageRoot)
        Dim DownloadFiles As New List(Of NetFile)
        For i = 0 To Pending.Count - 1
            Dim Entry = Pending(i)
            Entry.StagePath = Path.Combine(StageRoot, $"{i:D4}-{Entry.Sha256}.download")
            DownloadFiles.Add(New NetFile({Entry.DownloadUrl}, Entry.StagePath,
                New FileChecker With {.ActualSize = Entry.Size, .Hash = Entry.Sha256}))
        Next
        If DownloadFiles.Any Then
            Logger.Info($"[愚者更新] 需要下载 {DownloadFiles.Count} 个文件")
            Dim DownloadLoader As New LoaderDownload("下载愚者更新", DownloadFiles)
            Try
                DownloadLoader.WaitForExit(c:=Loader.CreateCancellationToken())
            Finally
                DownloadLoader.Cancel()
            End Try
        End If
        Loader.Progress = 0.76

        For Each Entry In Pending
            Dim ErrorText = (New FileChecker With {.ActualSize = Entry.Size, .Hash = Entry.Sha256}).Check(Entry.StagePath)
            If ErrorText IsNot Nothing Then Throw New Exception("$愚者更新下载校验失败，已阻止启动：" & Entry.RelativePath & vbCrLf & ErrorText)
        Next

        Dim CurrentPaths = New HashSet(Of String)(ManifestFiles.Select(Function(File) File.RelativePath), StringComparer.OrdinalIgnoreCase)
        Dim UnexpectedModPaths = FindUnexpectedActiveMods(InstanceRoot, CurrentPaths)
        Dim ObsoletePaths = PreviousPaths.Where(Function(Relative) Not CurrentPaths.Contains(Relative)).ToList
        ObsoletePaths.AddRange(ExplicitObsoletePaths)
        ObsoletePaths.AddRange(UnexpectedModPaths)
        ObsoletePaths = ObsoletePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList
        ApplyFoolUpdate(InstanceRoot, Pending, ObsoletePaths, Payload, PayloadSha256, StatePath)
        Loader.Progress = 1
        If Pending.Any OrElse ObsoletePaths.Any Then
            Hint($"愚者已更新至 {Payload.Value(Of String)("packVersion")}！", HintType.Green)
            If UnexpectedModPaths.Any Then
                Hint($"已将 {UnexpectedModPaths.Count} 个清单外的活动 Mod 移入可恢复备份。", HintType.Blue)
            End If
        Else
            Logger.Info($"[愚者更新] 客户端已是 {Payload.Value(Of String)("packVersion")}，SHA-256 校验通过")
        End If
    End Sub

    Private Function ParseFoolSignedEnvelope(EnvelopeText As String, ByRef PayloadBytes As Byte()) As JObject
        Dim Envelope = JObject.Parse(EnvelopeText)
        If Envelope.Value(Of Integer?)("schema") <> 1 Then Throw New FormatException("不支持的签名信封版本")
        PayloadBytes = Convert.FromBase64String(Envelope.Value(Of String)("payload"))
        Dim SignatureBytes = Convert.FromBase64String(Envelope.Value(Of String)("signature"))
        Dim SignatureValid = False
        For Each PublicKeyXml In FoolTrustedPublicKeysXml
            Try
                Using Rsa As New RSACryptoServiceProvider()
                    Rsa.PersistKeyInCsp = False
                    Rsa.FromXmlString(PublicKeyXml)
                    If Rsa.VerifyData(PayloadBytes, CryptoConfig.MapNameToOID("SHA256"), SignatureBytes) Then
                        SignatureValid = True
                        Exit For
                    End If
                End Using
            Catch ex As CryptographicException
                Logger.Warn(ex, "[愚者更新] 一个受信任签名密钥无法验证当前文档")
            End Try
        Next
        If Not SignatureValid Then Throw New CryptographicException("数字签名不正确")
        Return JObject.Parse(Encoding.UTF8.GetString(PayloadBytes))
    End Function

    Private Function BuildManagedDiscoveryUrl(ServerAddress As String) As String
        Dim Host As String = Nothing
        Dim Port As Integer
        If Not TrySplitMinecraftServerAddress(ServerAddress, Host, Port) Then
            Throw New FormatException("无法从玩家输入中解析服务器主机名：" & ServerAddress)
        End If
        Dim HostType = Uri.CheckHostName(Host)
        If HostType = UriHostNameType.Unknown Then Throw New FormatException("服务器主机名不能用于安全发现：" & Host)
        Dim UriHost = If(HostType = UriHostNameType.IPv6, "[" & Host & "]", Host)
        Return $"https://{UriHost}:{ManagedDiscoveryPort}{ManagedDiscoveryPath}"
    End Function

    Private Function ServerAddressesEqual(Left As String, Right As String) As Boolean
        Dim LeftHost As String = Nothing, RightHost As String = Nothing
        Dim LeftPort As Integer, RightPort As Integer
        Return TrySplitMinecraftServerAddress(Left, LeftHost, LeftPort) AndAlso
            TrySplitMinecraftServerAddress(Right, RightHost, RightPort) AndAlso
            LeftHost.Equals(RightHost, StringComparison.OrdinalIgnoreCase) AndAlso LeftPort = RightPort
    End Function

    Private Function TrySplitMinecraftServerAddress(Address As String, ByRef Host As String, ByRef Port As Integer) As Boolean
        Host = ""
        Port = 25565
        Dim Value = If(Address, "").Trim
        If Not Value.Any Then Return False
        Try
            If Value.StartsWithF("[") Then
                Dim ClosingBracket = Value.IndexOf("]"c)
                If ClosingBracket <= 1 Then Return False
                Host = Value.Substring(1, ClosingBracket - 1)
                Dim Suffix = Value.Substring(ClosingBracket + 1)
                If Suffix.Any Then
                    If Not Suffix.StartsWithF(":") OrElse Not Integer.TryParse(Suffix.Substring(1), Port) Then Return False
                End If
            ElseIf Value.Count(Function(Character) Character = ":"c) = 1 Then
                Dim Colon = Value.LastIndexOf(":"c)
                Host = Value.Substring(0, Colon)
                If Not Integer.TryParse(Value.Substring(Colon + 1), Port) Then Return False
            Else
                Host = Value
            End If
            Host = Host.Trim.TrimEnd("."c).ToLowerInvariant
            Return Host.Any AndAlso Port >= 1 AndAlso Port <= 65535
        Catch
            Return False
        End Try
    End Function

    Private Function FoolInstanceIsIsolated(Instance As McInstance) As Boolean
        Dim InstanceRoot = Path.GetFullPath(Instance.PathIndie).TrimEnd("\"c, "/"c)
        Dim MinecraftRoot = Path.GetFullPath(McFolderSelected).TrimEnd("\"c, "/"c)
        Return Not InstanceRoot.Equals(MinecraftRoot, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub ValidateFoolRuntime(Payload As JObject)
        Dim RequiredMinecraft = If(Payload.Value(Of String)("minecraft"), "")
        Dim LoaderInfo = TryCast(Payload("loader"), JObject)
        If LoaderInfo Is Nothing Then Throw New Exception("$愚者更新清单缺少加载器信息，已阻止启动。")
        Dim RequiredLoader = If(LoaderInfo.Value(Of String)("type"), "").Lower
        Dim RequiredLoaderVersion = If(LoaderInfo.Value(Of String)("version"), "")
        Dim LocalLoader As String = If(McInstanceSelected.Version.HasForge, "forge", If(McInstanceSelected.Version.HasNeoForge, "neoforge", If(McInstanceSelected.Version.HasFabric, "fabric", "")))
        Dim LocalLoaderVersion As String = If(LocalLoader = "forge", McInstanceSelected.Version.Forge, If(LocalLoader = "neoforge", McInstanceSelected.Version.NeoForge, McInstanceSelected.Version.Fabric))
        If McInstanceSelected.Version.VanillaName <> RequiredMinecraft OrElse LocalLoader <> RequiredLoader OrElse LocalLoaderVersion <> RequiredLoaderVersion Then
            Throw New Exception("$愚者服务器已经要求新的运行环境，当前启动器原型不会覆盖现有实例。" & vbCrLf &
                $"服务器要求：Minecraft {RequiredMinecraft} + {RequiredLoader} {RequiredLoaderVersion}" & vbCrLf &
                $"当前实例：Minecraft {McInstanceSelected.Version.VanillaName} + {LocalLoader} {LocalLoaderVersion}" & vbCrLf &
                "请等待管理员发布自动迁移包；旧实例与存档没有被修改。")
        End If
    End Sub

    Private Function ParseFoolFiles(Payload As JObject, ManifestUrl As String, InstanceRoot As String) As List(Of FoolManagedFile)
        Dim FileArray = TryCast(Payload("files"), JArray)
        If FileArray Is Nothing Then Throw New Exception("$愚者更新清单缺少文件列表，已阻止启动。")
        Dim Result As New List(Of FoolManagedFile)
        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each Token As JToken In FileArray
            Dim Entry = TryCast(Token, JObject)
            If Entry Is Nothing Then Throw New FormatException("文件项格式不正确")
            Dim Relative = NormalizeFoolPath(Entry.Value(Of String)("path"))
            If Not Seen.Add(Relative) Then Throw New FormatException("文件路径重复：" & Relative)
            Dim Sha256 = If(Entry.Value(Of String)("sha256"), "").Lower
            If Not Sha256.RegexCheck("^[0-9a-f]{64}$") Then Throw New FormatException("文件 SHA-256 不正确：" & Relative)
            Dim Size = Entry.Value(Of Long?)("size")
            If Not Size.HasValue OrElse Size.Value < 0 Then Throw New FormatException("文件大小不正确：" & Relative)
            Dim RelativeUrl = If(Entry.Value(Of String)("url"), "")
            Dim DownloadUri As New Uri(New Uri(ManifestUrl), RelativeUrl)
            If DownloadUri.Scheme <> Uri.UriSchemeHttps Then Throw New FormatException("文件下载地址不是 HTTPS：" & Relative)
            Result.Add(New FoolManagedFile With {
                .RelativePath = Relative,
                .TargetPath = ResolveFoolTarget(InstanceRoot, Relative),
                .DownloadUrl = DownloadUri.AbsoluteUri,
                .Sha256 = Sha256,
                .Size = Size.Value
            })
        Next
        Return Result
    End Function

    Private Function ParseFoolRemovals(Payload As JObject, InstanceRoot As String) As List(Of FoolManagedFile)
        Dim Result As New List(Of FoolManagedFile)
        Dim FileArray = TryCast(Payload("removeFiles"), JArray)
        If FileArray Is Nothing Then Return Result
        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each Token As JToken In FileArray
            Dim Entry = TryCast(Token, JObject)
            If Entry Is Nothing Then Throw New FormatException("移除项格式不正确")
            Dim Relative = NormalizeFoolPath(Entry.Value(Of String)("path"))
            If Not Seen.Add(Relative) Then Throw New FormatException("移除路径重复：" & Relative)
            Dim Sha256 = If(Entry.Value(Of String)("sha256"), "").Lower
            If Not Sha256.RegexCheck("^[0-9a-f]{64}$") Then Throw New FormatException("移除文件 SHA-256 不正确：" & Relative)
            Result.Add(New FoolManagedFile With {
                .RelativePath = Relative,
                .TargetPath = ResolveFoolTarget(InstanceRoot, Relative),
                .Sha256 = Sha256
            })
        Next
        Return Result
    End Function

    Private Function FindUnexpectedActiveMods(InstanceRoot As String, ExpectedPaths As HashSet(Of String)) As List(Of String)
        Dim Result As New List(Of String)
        Dim ModsRoot = Path.Combine(InstanceRoot, "mods")
        If Not Directory.Exists(ModsRoot) Then Return Result
        For Each FullPath In Directory.GetFiles(ModsRoot, "*", SearchOption.TopDirectoryOnly)
            If Not FullPath.EndsWithF(".jar", True) Then Continue For
            Dim Relative = NormalizeFoolPath("mods\" & Path.GetFileName(FullPath))
            If Not ExpectedPaths.Contains(Relative) Then
                Logger.Warn("[愚者更新] 发现清单外活动 Mod，将在备份后隔离：" & Relative)
                Result.Add(Relative)
            End If
        Next
        Return Result
    End Function

    Private Function NormalizeFoolPath(RawPath As String) As String
        Dim Relative = If(RawPath, "").Replace("/", "\").TrimStart("\"c)
        If Relative = "" OrElse Relative.Contains(":") OrElse Relative.Split("\"c).Any(Function(Part) Part = "" OrElse Part = "." OrElse Part = "..") Then
            Throw New FormatException("不安全的文件路径：" & RawPath)
        End If
        Dim Top = Relative.Split("\"c)(0).Lower
        If Not FoolAllowedRoots.Contains(Top) Then Throw New FormatException("不允许管理的目录：" & RawPath)
        Return Relative
    End Function

    Private Function ResolveFoolTarget(InstanceRoot As String, Relative As String) As String
        Dim Root = Path.GetFullPath(InstanceRoot).TrimEnd("\"c) & "\"
        Dim Target = Path.GetFullPath(Path.Combine(Root, Relative))
        If Not Target.StartsWith(Root, StringComparison.OrdinalIgnoreCase) Then Throw New FormatException("文件路径越界：" & Relative)
        Return Target
    End Function

    Private Function ReadFoolState(StatePath As String) As JObject
        If Not FileUtils.Exists(StatePath) Then Return Nothing
        Try
            Return JObject.Parse(FileUtils.ReadAsString(StatePath))
        Catch ex As Exception
            Logger.Warn(ex, "[愚者更新] 无法读取旧状态，将执行完整校验")
            Return Nothing
        End Try
    End Function

    Private Function ReadFoolManagedPaths(State As JObject) As HashSet(Of String)
        Dim Result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim Paths = TryCast(State?("files"), JArray)
        If Paths Is Nothing Then Return Result
        For Each Token In Paths
            Try
                Result.Add(NormalizeFoolPath(Token.ToString))
            Catch ex As Exception
                Logger.Warn(ex, "[愚者更新] 忽略旧状态中的不安全路径")
            End Try
        Next
        Return Result
    End Function

    Private Sub ApplyFoolUpdate(InstanceRoot As String, Pending As List(Of FoolManagedFile), Obsolete As List(Of String),
                                Payload As JObject, PayloadSha256 As String, StatePath As String)
        Dim Stamp = Date.Now.ToString("yyyyMMdd-HHmmss")
        Dim BackupRoot = Path.Combine(InstanceRoot, "PCL", "FoolUpdate", "backups", Stamp)
        Dim Affected As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each Entry In Pending
            Affected.Add(Entry.RelativePath)
        Next
        For Each Relative In Obsolete
            Affected.Add(Relative)
        Next

        For Each Relative In Affected
            Dim Target = ResolveFoolTarget(InstanceRoot, Relative)
            If FileUtils.Exists(Target) Then
                Dim Backup = Path.Combine(BackupRoot, Relative)
                Directory.CreateDirectory(Path.GetDirectoryName(Backup))
                File.Copy(Target, Backup, True)
            End If
        Next

        Try
            For Each Entry In Pending
                Dim Target = Entry.TargetPath
                Directory.CreateDirectory(Path.GetDirectoryName(Target))
                Dim NewFile = Target & ".fool-update-new"
                File.Copy(Entry.StagePath, NewFile, True)
                If FileUtils.Exists(Target) Then
                    File.Replace(NewFile, Target, Nothing, True)
                Else
                    File.Move(NewFile, Target)
                End If
            Next
            For Each Relative In Obsolete
                Dim Target = ResolveFoolTarget(InstanceRoot, Relative)
                If FileUtils.Exists(Target) Then File.Delete(Target)
            Next

            Dim StateFiles As New JArray
            For Each Token As JToken In CType(Payload("files"), JArray)
                StateFiles.Add(Token("path").ToString.Replace("/", "\"))
            Next
            Dim State As New JObject From {
                {"schema", 1},
                {"packId", FoolPackId},
                {"packVersion", Payload.Value(Of String)("packVersion")},
                {"payloadSha256", PayloadSha256},
                {"updatedAt", Date.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")},
                {"files", StateFiles}
            }
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath))
            FileUtils.Write(StatePath, State.ToString(Formatting.Indented), encoding:=New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Error(ex, "[愚者更新] 应用失败，开始回滚")
            For Each Relative In Affected
                Try
                    Dim Target = ResolveFoolTarget(InstanceRoot, Relative)
                    Dim Backup = Path.Combine(BackupRoot, Relative)
                    If FileUtils.Exists(Backup) Then
                        Directory.CreateDirectory(Path.GetDirectoryName(Target))
                        File.Copy(Backup, Target, True)
                    ElseIf FileUtils.Exists(Target) Then
                        File.Delete(Target)
                    End If
                Catch RollbackEx As Exception
                    Logger.Error(RollbackEx, "[愚者更新] 回滚文件失败：" & Relative)
                End Try
            Next
            Throw New Exception("$愚者更新应用失败，已尝试自动回滚并阻止启动。请将 PCL 日志发给服务器管理员。", ex)
        End Try
    End Sub

End Module
