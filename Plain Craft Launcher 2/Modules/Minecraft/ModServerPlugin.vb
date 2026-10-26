Public Class ServerPluginSearchResult
    Public ProjectId As String
    Public Slug As String
    Public Title As String
    Public Description As String
    Public Author As String
    Public IconUrl As String
    Public DownloadCount As Long
    Public Loaders As New List(Of String)
    Public GameVersions As New List(Of String)
    ''' <summary>来源标识。为空表示 Modrinth 官方接口，否则为麦块开放 API 的来源段（modrinth / spigot / curseforge）。</summary>
    Public SourceKey As String = ""
    ''' <summary>来源展示名称。</summary>
    Public SourceName As String = "Modrinth"
    ''' <summary>来源网站上的详情页，用于查看详情或在无法自动下载时手动下载。</summary>
    Public ExternalUrl As String = ""

    Public ReadOnly Property Website As String
        Get
            If Not String.IsNullOrWhiteSpace(ExternalUrl) Then Return ExternalUrl
            Return $"https://modrinth.com/plugin/{Slug}"
        End Get
    End Property
End Class

Public Class ServerPluginFile
    Public ProjectId As String
    Public ProjectTitle As String
    Public VersionNumber As String
    Public FileName As String
    Public Url As String
    Public MirrorUrl As String
    Public Hash As String
    Public Size As Long = -1
    Public GameVersion As String
    Public Loader As String
End Class

Public Module ModServerPlugin

    Private ReadOnly PluginLoaders As String() = {"bukkit", "folia", "paper", "purpur", "spigot"}

    Public Function SearchModrinthServerPlugins(SearchText As String, MinecraftVersion As String,
                                                Optional Limit As Integer = 20) As List(Of ServerPluginSearchResult)
        If String.IsNullOrWhiteSpace(MinecraftVersion) Then Throw New ArgumentException("必须指定 Minecraft 版本。")
        Dim Facets As String =
            $"[[""project_type:plugin""],[""versions:{StringUtils.UrlEscape(MinecraftVersion.Trim)}""]]"
        Dim Address As String =
            $"https://api.modrinth.com/v2/search?limit={Math.Max(1, Math.Min(50, Limit))}&index=relevance" &
            "&facets=" & StringUtils.UrlEscape(Facets)
        If Not String.IsNullOrWhiteSpace(SearchText) Then Address &= "&query=" & StringUtils.UrlEscape(SearchText.Trim)

        Dim Json As JObject = DlModRequest(Address)
        Dim Result As New List(Of ServerPluginSearchResult)
        For Each Hit As JObject In Json("hits")
            Dim Loaders = If(Hit("categories")?.Select(Function(token) token.ToString), {}).
                Where(Function(name) PluginLoaders.Contains(name, StringComparer.OrdinalIgnoreCase)).
                Select(Function(name) name.ToLowerInvariant).Distinct.ToList
            If Not Loaders.Any() Then Continue For
            Dim AuthorText As String = If(Hit("organization")?.ToString, "").Trim
            If AuthorText = "" Then AuthorText = If(Hit("author")?.ToString, "").Trim
            Result.Add(New ServerPluginSearchResult With {
                .ProjectId = Hit("project_id").ToString,
                .Slug = Hit("slug").ToString,
                .Title = Hit("title").ToString,
                .Description = Hit("description").ToString,
                .Author = AuthorText,
                .IconUrl = If(Hit("icon_url")?.ToString, ""),
                .DownloadCount = If(Hit("downloads") Is Nothing, 0L, Hit("downloads").ToObject(Of Long)),
                .Loaders = Loaders,
                .GameVersions = If(Hit("versions")?.Select(Function(token) token.ToString), {}).ToList
            })
        Next
        Return Result
    End Function

    Public Function GetLatestModrinthPluginFile(Project As ServerPluginSearchResult, MinecraftVersion As String,
                                                CoreType As ServerCoreType,
                                                Optional Folder As String = Nothing,
                                                Optional Config As JObject = Nothing) As ServerPluginFile
        If String.IsNullOrWhiteSpace(MinecraftVersion) Then Throw New ArgumentException("必须指定 Minecraft 版本。")
        Dim AcceptedLoaders = GetAcceptedPluginLoaders(CoreType, Folder, Config)
        If Not AcceptedLoaders.Any() Then Throw New Exception("当前服务端核心不支持 Bukkit 插件。")

        Dim Versions As JArray = DlModRequest(
            $"https://api.modrinth.com/v2/project/{StringUtils.UrlEscape(Project.ProjectId)}/version?include_changelog=false")
        For Each Version As JObject In Versions.
            OrderByDescending(Function(entry) entry("date_published")?.ToString, StringComparer.Ordinal).
            ToList
            Dim GameVersions = If(Version("game_versions")?.Select(Function(token) token.ToString), {}).
                ToList
            If Not GameVersions.Any(Function(name) String.Equals(name, MinecraftVersion.Trim, StringComparison.OrdinalIgnoreCase)) Then Continue For
            Dim VersionLoaders = If(Version("loaders")?.Select(Function(token) token.ToString), {}).
                Select(Function(name) name.ToLowerInvariant).ToList
            Dim Loader = AcceptedLoaders.FirstOrDefault(Function(name) VersionLoaders.Contains(name))
            If Loader Is Nothing Then Continue For

            Dim Files = If(Version("files"), New JArray).OfType(Of JObject)().
                Where(Function(candidate) If(candidate("filename")?.ToString, "").EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).
                ToList
            Dim File = Files.FirstOrDefault(Function(entry) entry("primary")?.ToObject(Of Boolean) = True)
            If File Is Nothing Then File = Files.FirstOrDefault
            If File Is Nothing OrElse File("url") Is Nothing Then Continue For
            If Not IsModrinthPluginFileAvailable(File("url").ToString) Then Continue For

            Dim Hash As String = If(File("hashes")?("sha512")?.ToString, "").ToLowerInvariant()
            If Hash = "" Then Hash = If(File("hashes")?("sha1")?.ToString, "").ToLowerInvariant()
            Dim SafeFileName As String = GetSafePluginFileName(File("filename").ToString)
            Return New ServerPluginFile With {
                .ProjectId = Project.ProjectId,
                .ProjectTitle = Project.Title,
                .VersionNumber = If(Version("version_number")?.ToString, ""),
                .FileName = SafeFileName,
                .Url = File("url").ToString,
                .MirrorUrl = DlSourceModGet(File("url").ToString),
                .Hash = If(Hash = "", Nothing, Hash),
                .Size = If(File("size") Is Nothing, -1L, File("size").ToObject(Of Long)),
                .GameVersion = MinecraftVersion.Trim,
                .Loader = Loader
            }
        Next
        Throw New Exception($"没有找到适用于 Minecraft {MinecraftVersion} 和当前服务端核心的插件文件。")
    End Function

    Private Function IsModrinthPluginFileAvailable(Url As String) As Boolean
        If String.IsNullOrWhiteSpace(Url) OrElse Not Url.ContainsIgnoreCase("cdn.modrinth.com/") Then Return False
        For Each Source In {Url, DlSourceModGet(Url)}.Distinct
            Try
                NetRequestByClient(Source, HttpMethod.Head, Timeout:=10000)
                Return True
            Catch
            End Try
        Next
        Logger.Warn($"Modrinth 插件文件不可用，将尝试更早的兼容版本：{Url}", LogBehavior.None)
        Return False
    End Function

    Public Function GetAcceptedPluginLoaders(CoreType As ServerCoreType,
                                             Optional Folder As String = Nothing,
                                             Optional Config As JObject = Nothing) As List(Of String)
        Select Case CoreType
            Case ServerCoreType.Paper
                Return New List(Of String) From {"paper", "bukkit", "spigot", "folia"}
            Case ServerCoreType.Purpur
                Return New List(Of String) From {"purpur", "paper", "bukkit", "spigot", "folia"}
            Case ServerCoreType.MSLMC
                If SupportsServerPlugins(Folder, Config) Then
                    Return New List(Of String) From {"paper", "purpur", "bukkit", "spigot", "folia"}
                End If
                Return New List(Of String)
            Case Else
                Return New List(Of String)
        End Select
    End Function

    Public Function FormatServerPluginDownloads(Count As Long) As String
        If Count >= 1000000 Then Return $"{Count / 1000000.0:0.#}M 次下载"
        If Count >= 1000 Then Return $"{Count / 1000.0:0.#}K 次下载"
        Return $"{Count} 次下载"
    End Function

    ''' <summary>
    ''' 麦块开放 API 根地址。文档：https://bbs.minekuai.com/open-api
    ''' 插件按来源拆成三个独立的资源类型，参数与响应同构。
    ''' </summary>
    Private Const MinekuaiApiRoot As String = "https://api.minekuai.cn"
    Private ReadOnly MinekuaiPluginSources As String() = {"modrinth", "spigot", "curseforge"}

    Public Function GetMinekuaiPluginSourceName(SourceKey As String) As String
        Select Case If(SourceKey, "").ToLowerInvariant()
            Case "modrinth"
                Return "麦块 · Modrinth"
            Case "spigot"
                Return "麦块 · Spigot"
            Case "curseforge"
                Return "麦块 · CurseForge"
            Case Else
                Return "麦块"
        End Select
    End Function

    ''' <summary>
    ''' 通过麦块开放 API 搜索服务端插件。
    ''' </summary>
    ''' <param name="SourceKey">来源段：modrinth / spigot / curseforge。</param>
    Public Function SearchMinekuaiServerPlugins(SearchText As String, MinecraftVersion As String, SourceKey As String,
                                                Optional Limit As Integer = 20) As List(Of ServerPluginSearchResult)
        If String.IsNullOrWhiteSpace(MinecraftVersion) Then Throw New ArgumentException("必须指定 Minecraft 版本。")
        SourceKey = If(SourceKey, "").ToLowerInvariant()
        If Not MinekuaiPluginSources.Contains(SourceKey) Then Throw New ArgumentException("未知的麦块插件来源：" & SourceKey)

        Dim Address As String = $"{MinekuaiApiRoot}/panel/public/mc-plugins/{SourceKey}?limit={Math.Max(1, Math.Min(50, Limit))}"
        If Not String.IsNullOrWhiteSpace(SearchText) Then Address &= "&q=" & StringUtils.UrlEscape(SearchText.Trim)
        'Spigot 来源的数据里没有 Minecraft 版本信息，带版本筛选会一直返回空列表
        If Not String.Equals(SourceKey, "spigot", StringComparison.OrdinalIgnoreCase) Then
            Address &= "&game_version[]=" & StringUtils.UrlEscape(MinecraftVersion.Trim)
        End If

        Dim Json As JObject = NetRequestByClientRetry(Address, Accept:="application/json", RequireJson:=True).DeserializeJson()
        Dim DisplayName As String = GetMinekuaiPluginSourceName(SourceKey)
        Dim Result As New List(Of ServerPluginSearchResult)
        For Each Item As JObject In If(Json("items"), New JArray).OfType(Of JObject)()
            Dim Slug As String = If(Item("slug")?.ToString, "")
            If String.IsNullOrWhiteSpace(Slug) Then Continue For
            Dim TitleText As String = If(Item("title_zh")?.ToString, "").Trim
            If TitleText = "" Then TitleText = If(Item("title")?.ToString, Slug)
            Dim DescriptionText As String = If(Item("description_zh")?.ToString, "").Trim
            If DescriptionText = "" Then DescriptionText = If(Item("description_en")?.ToString, "").Trim
            'categories 在不同来源里可能是 null、加载器名或人类可读分类名，只取能对上的加载器
            Dim CategoryToken As JToken = If(Item("categories") IsNot Nothing AndAlso Item("categories").Type = JTokenType.Array,
                                             Item("categories"), New JArray)
            Dim LoaderList = CategoryToken.Select(Function(token) token.ToString().ToLowerInvariant()).
                Where(Function(name) PluginLoaders.Contains(name, StringComparer.OrdinalIgnoreCase)).
                Distinct.ToList
            Result.Add(New ServerPluginSearchResult With {
                .ProjectId = If(Item("project_id")?.ToString, Slug),
                .Slug = Slug,
                .Title = TitleText,
                .Description = DescriptionText,
                .Author = DisplayName,
                .IconUrl = If(Item("icon_url")?.ToString, ""),
                .DownloadCount = If(Item("downloads") Is Nothing, 0L, Item("downloads").ToObject(Of Long)),
                .Loaders = LoaderList,
                .GameVersions = If(Item("game_versions")?.Select(Function(token) token.ToString()), {}).ToList,
                .SourceKey = SourceKey,
                .SourceName = DisplayName,
                .ExternalUrl = If(Item("external_url")?.ToString, "")
            })
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 通过麦块开放 API 获取指定插件在目标 MC 版本与核心下可用的最新文件。
    ''' 该组接口的版本是扁平结构，下载直链在 download_url 中；作者禁止分发时回退 external_url 提示手动下载。
    ''' </summary>
    Public Function GetLatestMinekuaiPluginFile(Project As ServerPluginSearchResult, MinecraftVersion As String,
                                                CoreType As ServerCoreType,
                                                Optional Folder As String = Nothing,
                                                Optional Config As JObject = Nothing) As ServerPluginFile
        If Project Is Nothing OrElse String.IsNullOrWhiteSpace(Project.Slug) Then Throw New ArgumentException("插件信息不完整。")
        If String.IsNullOrWhiteSpace(MinecraftVersion) Then Throw New ArgumentException("必须指定 Minecraft 版本。")
        Dim AcceptedLoaders = GetAcceptedPluginLoaders(CoreType, Folder, Config)
        If Not AcceptedLoaders.Any() Then Throw New Exception("当前服务端核心不支持 Bukkit 插件。")

        Dim SourceKey As String = If(String.IsNullOrWhiteSpace(Project.SourceKey), "modrinth", Project.SourceKey).ToLowerInvariant()
        'Spigot 的下载链接会跳转到需要登录的网页，无法自动下载
        If SourceKey = "spigot" Then
            Throw New Exception("Spigot 来源需要在网页上手动下载插件。" &
                                If(String.IsNullOrWhiteSpace(Project.ExternalUrl), "", vbCrLf & "资源页面：" & Project.ExternalUrl))
        End If
        Dim Address As String = $"{MinekuaiApiRoot}/panel/public/mc-plugins/{SourceKey}/detail/{StringUtils.UrlEscape(Project.Slug)}/versions"
        Dim Versions As JArray = NetRequestByClientRetry(Address, Accept:="application/json", RequireJson:=True).DeserializeJson()
        Dim ExternalHint As String = If(String.IsNullOrWhiteSpace(Project.ExternalUrl), "",
                                        $"{vbCrLf}可在浏览器中手动下载：{Project.ExternalUrl}")

        For Each Entry As JObject In Versions.OfType(Of JObject)().
            OrderByDescending(Function(item) If(item("date_published")?.ToString, ""), StringComparer.Ordinal).ToList
            Dim GameVersions = If(Entry("game_versions")?.Select(Function(token) token.ToString()), {}).ToList
            If GameVersions.Any() AndAlso
               Not GameVersions.Any(Function(name) String.Equals(name, MinecraftVersion.Trim, StringComparison.OrdinalIgnoreCase)) Then Continue For
            Dim VersionLoaders = If(Entry("loaders")?.Select(Function(token) token.ToString().ToLowerInvariant()), {}).ToList
            Dim MatchedLoader As String = ""
            If VersionLoaders.Any() Then
                MatchedLoader = AcceptedLoaders.FirstOrDefault(Function(name) VersionLoaders.Contains(name))
                If MatchedLoader Is Nothing Then Continue For
            End If
            Dim DownloadUrl As String = If(Entry("download_url")?.ToString, "")
            If String.IsNullOrWhiteSpace(DownloadUrl) Then Continue For '作者禁止第三方分发等情况
            Dim VersionNumber As String = If(Entry("version_number")?.ToString, "")
            Dim FileName As String = If(Entry("filename")?.ToString, "")
            If Not FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) Then
                'Spigot 等来源没有文件名，按 slug + 版本号生成
                FileName = Project.Slug.Replace(" ", "-") & "-" & If(String.IsNullOrWhiteSpace(VersionNumber), "latest", VersionNumber) & ".jar"
            End If
            Return New ServerPluginFile With {
                .ProjectId = Project.ProjectId,
                .ProjectTitle = Project.Title,
                .VersionNumber = VersionNumber,
                .FileName = GetSafePluginFileName(FileName),
                .Url = DownloadUrl,
                .MirrorUrl = Nothing,
                .Hash = Nothing,
                .Size = -1,
                .GameVersion = MinecraftVersion.Trim,
                .Loader = MatchedLoader
            }
        Next
        Throw New Exception($"没有找到适用于 Minecraft {MinecraftVersion} 和当前服务端核心的插件文件。" & ExternalHint)
    End Function

    Private Function GetSafePluginFileName(Value As String) As String
        Dim Result As String = PathUtils.GetLastPart(Value).Trim.TrimEnd("."c)
        For Each InvalidChar As Char In Path.GetInvalidFileNameChars()
            Result = Result.Replace(InvalidChar, "_"c)
        Next
        If String.IsNullOrWhiteSpace(Result) OrElse Not Result.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) Then
            Throw New Exception("接口返回了无效的插件文件名。")
        End If
        Return Result
    End Function

End Module
