Public Class MslmcCoreEntry
    Public Name As String
    Public Description As String
    Public Versions As New List(Of String)

    Public ReadOnly Property IsHybrid As Boolean
        Get
            Return ModServerMslmc.IsHybridCore(Name)
        End Get
    End Property

    Public ReadOnly Property SupportsPlugins As Boolean
        Get
            Return ModServerMslmc.SupportsPluginsForCore(Name)
        End Get
    End Property

    Public ReadOnly Property SupportsMods As Boolean
        Get
            Return ModServerMslmc.SupportsModsForCore(Name)
        End Get
    End Property

    Public ReadOnly Property DisplayName As String
        Get
            Return Name & If(IsHybrid, "（插件 + Mod）", "")
        End Get
    End Property
End Class

Public Class MslmcBuildEntry
    Public CoreName As String
    Public MinecraftVersion As String
    Public BuildName As String
End Class

Public Class MslmcDownloadEntry
    Public CoreName As String
    Public MinecraftVersion As String
    Public BuildName As String
    Public FileName As String
    Public DownloadUrl As String
    Public Sha256 As String
End Class

Public Module ModServerMslmc

    Private Const ApiRoot As String = "https://api.mslmc.cn/v4"
    Private Const MinRequestIntervalMs As Integer = 1500
    Private ReadOnly RequestLock As New Object
    Private ReadOnly CoreCacheLock As New Object
    Private ReadOnly VersionCacheLock As New Object
    Private ReadOnly BuildCacheLock As New Object
    Private LastRequestTime As Date = Date.MinValue
    Private CachedCoreNames As List(Of String) = Nothing
    Private ReadOnly VersionCache As New Dictionary(Of String, MslmcCoreEntry)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly BuildCache As New Dictionary(Of String, List(Of MslmcBuildEntry))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly HybridCores As New HashSet(Of String)({
        "arclight-forge", "arclight-fabric", "arclight-neoforge",
        "youer", "mohist", "catserver", "spongeforge", "spongeneo"
    }, StringComparer.OrdinalIgnoreCase)
    Private ReadOnly PluginCores As New HashSet(Of String)({
        "paper", "purpur", "leaf", "leaves", "spigot", "folia", "lightfall",
        "pufferfish", "pufferfish_purpur", "bukkit",
        "arclight-forge", "arclight-fabric", "arclight-neoforge", "youer", "mohist", "catserver"
    }, StringComparer.OrdinalIgnoreCase)
    Private ReadOnly ModCores As New HashSet(Of String)({
        "neoforge", "forge", "fabric", "quilt",
        "arclight-forge", "arclight-fabric", "arclight-neoforge", "youer", "mohist", "catserver",
        "spongeforge", "spongeneo"
    }, StringComparer.OrdinalIgnoreCase)
    Private ReadOnly UnsupportedJavaCores As New HashSet(Of String)({
        "bedrock-server", "nukkitx"
    }, StringComparer.OrdinalIgnoreCase)
    Private ReadOnly RequestHeaders As String(,) = {
        {"User-Agent", "PCL-TLSver/2.99 MSLMC-Client"},
        {"Accept", "application/json"}
    }

    Public Function IsHybridCore(CoreName As String) As Boolean
        Return HybridCores.Contains(CoreName)
    End Function

    Public Function SupportsPluginsForCore(CoreName As String) As Boolean
        Return PluginCores.Contains(CoreName)
    End Function

    Public Function SupportsModsForCore(CoreName As String) As Boolean
        Return ModCores.Contains(CoreName)
    End Function

    Public Function GetMslmcCoreNames(Optional ForceRefresh As Boolean = False) As List(Of String)
        If Not ForceRefresh Then
            SyncLock CoreCacheLock
                If CachedCoreNames IsNot Nothing Then Return CachedCoreNames
            End SyncLock
        End If
        Dim Json As JObject = MslmcRequest("/mirrors?view=list")
        Dim Result As List(Of String) = Json("data").Select(Function(item) item.ToString()).
            Where(Function(name) Not UnsupportedJavaCores.Contains(name)).
            Distinct(StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(name) If(IsHybridCore(name), 0, 1)).
            ThenBy(Function(name) name, StringComparer.OrdinalIgnoreCase).
            ToList
        SyncLock CoreCacheLock
            CachedCoreNames = Result
        End SyncLock
        Return Result
    End Function

    Public Function GetMslmcCore(CoreName As String, Optional ForceRefresh As Boolean = False) As MslmcCoreEntry
        If Not ForceRefresh Then
            SyncLock VersionCacheLock
                Dim Cached As MslmcCoreEntry = Nothing
                If VersionCache.TryGetValue(CoreName, Cached) Then Return Cached
            End SyncLock
        End If
        Dim Json As JObject = MslmcRequest($"/mirrors/{StringUtils.UrlEscape(CoreName)}")
        Dim Data = Json("data")
        Dim Result As New MslmcCoreEntry With {
            .Name = CoreName,
            .Description = Data("description")?.ToString,
            .Versions = If(Data("versions")?.Select(Function(item) item.ToString), {}).ToList
        }
        SyncLock VersionCacheLock
            VersionCache(CoreName) = Result
        End SyncLock
        Return Result
    End Function

    Public Function GetMslmcBuilds(CoreName As String, MinecraftVersion As String,
                                    Optional ForceRefresh As Boolean = False) As List(Of MslmcBuildEntry)
        Dim CacheKey As String = $"{CoreName}|{MinecraftVersion}"
        If Not ForceRefresh Then
            SyncLock BuildCacheLock
                Dim Cached As List(Of MslmcBuildEntry) = Nothing
                If BuildCache.TryGetValue(CacheKey, Cached) Then Return Cached
            End SyncLock
        End If
        Dim Json As JObject = MslmcRequest(
            $"/mirrors/{StringUtils.UrlEscape(CoreName)}/{StringUtils.UrlEscape(MinecraftVersion)}?view=list")
        Dim Result As New List(Of MslmcBuildEntry)
        For Each BuildToken As JToken In Json("data")
            Result.Add(New MslmcBuildEntry With {
                .CoreName = CoreName,
                .MinecraftVersion = MinecraftVersion,
                .BuildName = BuildToken.ToString
            })
        Next
        SyncLock BuildCacheLock
            BuildCache(CacheKey) = Result
        End SyncLock
        Return Result
    End Function

    Public Sub ClearMslmcCache()
        SyncLock VersionCacheLock
            VersionCache.Clear()
        End SyncLock
        SyncLock BuildCacheLock
            BuildCache.Clear()
        End SyncLock
    End Sub

    Public Function GetMslmcDownload(CoreName As String, MinecraftVersion As String,
                                     BuildName As String) As MslmcDownloadEntry
        Dim Json As JObject = MslmcRequest(
            $"/download/server/{StringUtils.UrlEscape(CoreName)}/{StringUtils.UrlEscape(MinecraftVersion)}" &
            $"?build={StringUtils.UrlEscape(BuildName)}")
        Dim Data = Json("data")
        Dim Url As String = Data("url")?.ToString
        Return New MslmcDownloadEntry With {
            .CoreName = CoreName,
            .MinecraftVersion = MinecraftVersion,
            .BuildName = BuildName,
            .DownloadUrl = Url,
            .FileName = PathUtils.GetLastPart(Url),
            .Sha256 = Data("sha256")?.ToString
        }
    End Function

    Private Function MslmcRequest(Path As String) As JObject
        SyncLock RequestLock
            Dim Remaining As Double = MinRequestIntervalMs - (Date.UtcNow - LastRequestTime).TotalMilliseconds
            If Remaining > 0 Then Thread.Sleep(CInt(Math.Ceiling(Remaining)))
            Dim Url As String = ApiRoot & Path
            Try
                Logger.Info("请求 MSLMC API：" & Url)
                Dim Result As JObject = NetRequestByClient(
                    Url, Headers:=RequestHeaders, Timeout:=15000, RequireJson:=True).DeserializeJson()
                If Result("code")?.ToObject(Of Integer) <> 200 Then
                    Throw New Exception("MSLMC 返回失败：" & If(Result("message")?.ToString, Result("code")?.ToString))
                End If
                Return Result
            Finally
                LastRequestTime = Date.UtcNow
            End Try
        End SyncLock
    End Function

End Module
