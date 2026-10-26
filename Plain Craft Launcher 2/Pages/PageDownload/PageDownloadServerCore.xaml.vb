Public Class PageDownloadServerCore

    Private Class ServerVersionOption
        Public MinecraftVersion As String
        Public Metadata As Object
    End Class

    Private Class ServerDownloadInfo
        Public Url As String
        Public FallbackUrl As String
        Public FileName As String
        Public Hash As String
        Public Size As Long = -1
        Public IsInstaller As Boolean
        Public CoreType As ServerCoreType
        Public LoaderVersion As String
        Public LocalPath As String
        Public MslmcCore As String
    End Class

    Private IsLoadedOnce As Boolean = False
    Private IsPopulatingVersions As Boolean = False
    Private IsPopulatingMslmcCores As Boolean = False
    Private IsMslmcCoreLoading As Boolean = False
    Private IsMslmcBuildLoading As Boolean = False
    Private VersionLoadId As Integer = 0
    Private BuildLoadId As Integer = 0
    Private IsServerFolderNameCustom As Boolean = False
    Private IsUpdatingServerFolderName As Boolean = False
    Private LastSuggestedFolderName As String = ""
    Private ActiveServerDownload As LoaderCombo(Of Integer) = Nothing
    Private Headers As String(,) = {
        {"User-Agent", "PCL-TLSver/2.99 (https://github.com/ch-tls/Plain-Craft-Launcher-PCL-TLSver-Community)"},
        {"Accept", "application/json"}
    }

    Private Sub PageDownloadServerCore_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If IsLoadedOnce Then Return
        IsLoadedOnce = True
        UpdateMslmcCoreVisibility()
        LoadVersions()
    End Sub

    Private Sub ComboServerCoreType_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboServerCoreType.SelectionChanged
        If Not IsLoadedOnce Then Return
        BuildLoadId += 1
        UpdateMslmcCoreVisibility()
        If CType(Val(ComboServerCoreType.SelectedItem?.Tag), ServerCoreType) = ServerCoreType.MSLMC Then
            LoadMslmcCores(False)
        Else
            LoadVersions()
        End If
    End Sub

    Private Sub BtnRefreshServerCore_Click(sender As Object, e As EventArgs) Handles BtnRefreshServerCore.Click
        If CType(Val(ComboServerCoreType.SelectedItem?.Tag), ServerCoreType) = ServerCoreType.MSLMC Then
            LoadMslmcCores(True)
        Else
            LoadVersions()
        End If
    End Sub

    Private Sub ComboMslmcCore_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboMslmcCore.SelectionChanged
        If IsPopulatingMslmcCores OrElse ComboMslmcCore.SelectedItem Is Nothing Then Return
        BuildLoadId += 1
        LoadMslmcVersions()
    End Sub

    Private Sub UpdateMslmcCoreVisibility()
        Dim IsMslmc As Boolean = CType(Val(ComboServerCoreType.SelectedItem?.Tag), ServerCoreType) = ServerCoreType.MSLMC
        LabMslmcCore.Visibility = IsMslmc.ToVisibility
        ComboMslmcCore.Visibility = IsMslmc.ToVisibility
        RowMslmcCore.Height = New GridLength(If(IsMslmc, 40, 0))
        If Not IsMslmc Then
            IsPopulatingMslmcCores = False
            IsMslmcCoreLoading = False
            IsMslmcBuildLoading = False
            ComboMslmcCore.IsEnabled = True
            ComboServerVersion.IsEnabled = True
            BtnRefreshServerCore.IsEnabled = True
        End If
    End Sub

    Private Sub LoadMslmcCores(ForceRefresh As Boolean)
        If IsPopulatingMslmcCores Then Return
        If ForceRefresh Then ClearMslmcCache()
        VersionLoadId += 1
        Dim CurrentLoadId As Integer = VersionLoadId
        IsPopulatingMslmcCores = True
        ComboMslmcCore.Items.Clear()
        ComboServerVersion.Items.Clear()
        ComboServerBuild.Items.Clear()
        BtnDownloadServerCore.IsEnabled = False
        BtnRefreshServerCore.IsEnabled = False
        ComboMslmcCore.IsEnabled = False
        ComboServerVersion.IsEnabled = False
        HintServerDownload.Theme = MyHint.Themes.Blue
        HintServerDownload.Text = "正在获取 MSLMC 核心列表……"
        RunInNewThread(
        Sub()
            Try
                Dim Cores = GetMslmcCoreNames(ForceRefresh)
                RunInUi(
                Sub()
                    If CurrentLoadId <> VersionLoadId Then Return
                    IsPopulatingMslmcCores = False
                    ComboMslmcCore.Items.Clear()
                    For Each Core In Cores
                        ComboMslmcCore.Items.Add(New MyComboBoxItem With {
                            .Content = Core & If(IsHybridCore(Core), "（插件 + Mod）", ""),
                            .Tag = Core
                        })
                    Next
                    If ComboMslmcCore.Items.Count = 0 Then
                        ComboMslmcCore.IsEnabled = True
                        ComboServerVersion.IsEnabled = True
                        BtnRefreshServerCore.IsEnabled = True
                        HintServerDownload.Theme = MyHint.Themes.Yellow
                        HintServerDownload.Text = "MSLMC 没有返回可用核心。"
                        Return
                    End If
                    ComboMslmcCore.IsEnabled = True
                    BtnRefreshServerCore.IsEnabled = True
                    ComboMslmcCore.SelectedIndex = 0
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    If CurrentLoadId <> VersionLoadId Then Return
                    IsPopulatingMslmcCores = False
                    ComboMslmcCore.IsEnabled = True
                    ComboServerVersion.IsEnabled = True
                    BtnRefreshServerCore.IsEnabled = True
                    HintServerDownload.Theme = MyHint.Themes.Red
                    HintServerDownload.Text = "获取 MSLMC 核心列表失败：" & ex.GetDisplay(False)
                End Sub)
            End Try
        End Sub, "Load MSLMC Core List")
    End Sub

    Private Sub LoadMslmcVersions()
        If IsMslmcCoreLoading OrElse ComboMslmcCore.SelectedItem Is Nothing Then Return
        Dim CoreName As String = ComboMslmcCore.SelectedItem.Tag.ToString
        VersionLoadId += 1
        Dim CurrentLoadId As Integer = VersionLoadId
        IsMslmcCoreLoading = True
        ComboMslmcCore.IsEnabled = False
        ComboServerVersion.IsEnabled = False
        ComboServerVersion.Items.Clear()
        ComboServerBuild.Items.Clear()
        BtnDownloadServerCore.IsEnabled = False
        BtnRefreshServerCore.IsEnabled = False
        HintServerDownload.Theme = MyHint.Themes.Blue
        HintServerDownload.Text = $"正在获取 {CoreName} 的 MC 版本……"
        RunInNewThread(
        Sub()
            Try
                Dim Core = GetMslmcCore(CoreName)
                RunInUi(
                Sub()
                    If CurrentLoadId <> VersionLoadId Then Return
                    IsMslmcCoreLoading = False
                    ComboMslmcCore.IsEnabled = True
                    ComboServerVersion.IsEnabled = True
                    BtnRefreshServerCore.IsEnabled = True
                    For Each Version In Core.Versions
                        ComboServerVersion.Items.Add(New MyComboBoxItem With {
                            .Content = Version,
                            .Tag = New ServerVersionOption With {
                                .MinecraftVersion = Version,
                                .Metadata = CoreName
                            }
                        })
                    Next
                    If ComboServerVersion.Items.Count > 0 Then
                        ComboServerVersion.SelectedIndex = 0
                        HintServerDownload.Theme = MyHint.Themes.Blue
                        HintServerDownload.Text = $"{CoreName}：{Core.Description}"
                    Else
                        HintServerDownload.Theme = MyHint.Themes.Yellow
                        HintServerDownload.Text = "该核心没有提供可用 MC 版本。"
                    End If
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    If CurrentLoadId <> VersionLoadId Then Return
                    IsMslmcCoreLoading = False
                    ComboMslmcCore.IsEnabled = True
                    ComboServerVersion.IsEnabled = True
                    BtnRefreshServerCore.IsEnabled = True
                    HintServerDownload.Theme = MyHint.Themes.Red
                    HintServerDownload.Text = "获取 MSLMC 版本列表失败：" & ex.GetDisplay(False)
                End Sub)
            End Try
        End Sub, "Load MSLMC Versions")
    End Sub


    Private Sub LoadVersions()
        Dim CoreType As ServerCoreType = CType(Val(ComboServerCoreType.SelectedItem?.Tag), ServerCoreType)
        VersionLoadId += 1
        Dim CurrentLoadId As Integer = VersionLoadId
        IsPopulatingVersions = True
        ComboServerVersion.Items.Clear()
        ComboServerBuild.Items.Clear()
        BtnDownloadServerCore.IsEnabled = False
        HintServerDownload.Theme = MyHint.Themes.Blue
        HintServerDownload.Text = "正在获取服务端版本列表……"
        RunInNewThread(
        Sub()
            Try
                Dim Versions As New List(Of ServerVersionOption)
                Select Case CoreType
                    Case ServerCoreType.Vanilla
                        Dim Manifest As JObject = NetRequestByClientRetry(
                            "https://launchermeta.mojang.com/mc/game/version_manifest.json",
                            Accept:="application/json", RequireJson:=True).DeserializeJson()
                        For Each Entry As JObject In Manifest("versions")
                            Versions.Add(New ServerVersionOption With {
                                .MinecraftVersion = Entry("id").ToString, .Metadata = Entry})
                        Next
                    Case ServerCoreType.Paper
                        Dim Json As JObject = GetStationJson("https://fill.papermc.io/v3/projects/paper")
                        For Each VersionsGroup As JProperty In Json("versions")
                            For Each VersionToken As JToken In VersionsGroup.Value
                                Versions.Add(New ServerVersionOption With {.MinecraftVersion = VersionToken.ToString})
                            Next
                        Next
                    Case ServerCoreType.Purpur
                        Dim Json As JObject = GetStationJson("https://api.purpurmc.org/v2/purpur")
                        For Each VersionToken As JToken In Json("versions")
                            Versions.Add(New ServerVersionOption With {.MinecraftVersion = VersionToken.ToString})
                        Next
                        Versions.Reverse()
                    Case ServerCoreType.Fabric
                        Dim Json As JArray = GetStationJson("https://meta.fabricmc.net/v2/versions/game")
                        For Each Entry As JObject In Json
                            If Entry("stable")?.ToObject(Of Boolean) Then
                                Versions.Add(New ServerVersionOption With {.MinecraftVersion = Entry("version").ToString})
                            End If
                        Next
                    Case ServerCoreType.Forge
                        '官方 promotions_slim.json 返回 {"promos": {...}}，BMCLAPI /forge/promos 返回数组，
                        '两者结构不同，必须分开解析，否则回退到 BMCLAPI 时会抛 JArray → JObject 的转换异常。
                        Dim Promotions As List(Of (MinecraftVersion As String, IsRecommended As Boolean, Version As String))
                        Try
                            Promotions = ParseForgePromotions(
                                GetStationJson("https://files.minecraftforge.net/maven/net/minecraftforge/forge/promotions_slim.json"))
                        Catch
                            Promotions = ParseForgePromotions(
                                GetStationJson("https://bmclapi2.bangbang93.com/forge/promos"))
                        End Try
                        Dim ForgeVersions As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                        For Each Promotion In Promotions
                            If Promotion.IsRecommended OrElse Not ForgeVersions.ContainsKey(Promotion.MinecraftVersion) Then
                                ForgeVersions(Promotion.MinecraftVersion) = Promotion.Version
                            End If
                        Next
                        Dim MinecraftVersions = ForgeVersions.Keys.ToList
                        MinecraftVersions = MinecraftVersions.SortByComparison(AddressOf McUtils.CompareVersionGE)
                        For Each MinecraftVersion In MinecraftVersions
                            Versions.Add(New ServerVersionOption With {
                                .MinecraftVersion = MinecraftVersion, .Metadata = ForgeVersions(MinecraftVersion)})
                        Next
                    Case ServerCoreType.NeoForge
                        EnsureNeoForgeVersionsLoaded()
                        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                        For Each Entry In DlNeoForgeListLoader.Output.Value.
                            Where(Function(item) Not item.IsBeta)
                            If Seen.Add(Entry.Inherit) Then
                                Versions.Add(New ServerVersionOption With {.MinecraftVersion = Entry.Inherit})
                            End If
                        Next
                End Select
                RunInUi(
                Sub()
                    If CurrentLoadId <> VersionLoadId Then Return
                    IsPopulatingVersions = False
                    ComboServerVersion.Items.Clear()
                    For Each Version In Versions
                        ComboServerVersion.Items.Add(New MyComboBoxItem With {
                            .Content = Version.MinecraftVersion, .Tag = Version
                        })
                    Next
                    If ComboServerVersion.Items.Count > 0 Then
                        ComboServerVersion.SelectedIndex = 0
                        HintServerDownload.Text = $"已获取 {ComboServerVersion.Items.Count} 个 Minecraft 版本。"
                    Else
                        HintServerDownload.Theme = MyHint.Themes.Yellow
                        HintServerDownload.Text = "没有获取到可用版本。"
                    End If
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    If CurrentLoadId <> VersionLoadId Then Return
                    IsPopulatingVersions = False
                    HintServerDownload.Theme = MyHint.Themes.Red
                    HintServerDownload.Text = "获取版本列表失败：" & ex.Message
                End Sub)
            End Try
        End Sub, "Load Server Core Versions")
    End Sub

    Private Sub ComboServerVersion_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboServerVersion.SelectionChanged
        If IsPopulatingVersions OrElse ComboServerVersion.SelectedItem Is Nothing Then Return
        LoadBuilds()
    End Sub

    Private Sub LoadBuilds()
        Dim CoreType As ServerCoreType = CType(Val(ComboServerCoreType.SelectedItem?.Tag), ServerCoreType)
        Dim IsMslmcBuild As Boolean = CoreType = ServerCoreType.MSLMC
        If IsMslmcBuild Then
            If IsMslmcBuildLoading Then Return
            IsMslmcBuildLoading = True
            ComboMslmcCore.IsEnabled = False
            ComboServerVersion.IsEnabled = False
            BtnRefreshServerCore.IsEnabled = False
        End If
        Dim VersionOption = CType(ComboServerVersion.SelectedItem, MyComboBoxItem).Tag
        Dim Version As String = CType(VersionOption, ServerVersionOption).MinecraftVersion
        BuildLoadId += 1
        Dim CurrentLoadId As Integer = BuildLoadId
        ComboServerBuild.Items.Clear()
        BtnDownloadServerCore.IsEnabled = False
        If CoreType = ServerCoreType.Vanilla Then
            ComboServerBuild.Items.Add(New MyComboBoxItem With {.Content = "原版服务端", .Tag = Nothing})
            ComboServerBuild.SelectedIndex = 0
            BtnDownloadServerCore.IsEnabled = True
            HintServerDownload.Text = "将下载所选 Minecraft 版本的官方服务端。"
            UpdateSuggestedServerFolderName()
            Return
        End If

        HintServerDownload.Theme = MyHint.Themes.Blue
        HintServerDownload.Text = $"正在获取 {Version} 的构建列表……"
        RunInNewThread(
        Sub()
            Try
                Dim Builds As New List(Of (Name As String, Tag As Object))
                Dim BuildTotal As Integer = -1
                Select Case CoreType
                    Case ServerCoreType.Paper
                        Dim Json As JArray = GetStationJson(
                            $"https://fill.papermc.io/v3/projects/paper/versions/{StringUtils.UrlEscape(Version)}/builds")
                        For Each Entry As JObject In Json
                            Builds.Add(($"构建 {Entry("id")}（{Entry("channel")}）", Entry))
                        Next
                    Case ServerCoreType.Purpur
                        Dim Json As JObject = GetStationJson(
                            $"https://api.purpurmc.org/v2/purpur/{StringUtils.UrlEscape(Version)}")
                        For Each BuildToken As JToken In Json("builds")("all")
                            Builds.Add(($"{Version} - 构建 {BuildToken}", BuildToken.ToString))
                        Next
                        Builds.Reverse()
                    Case ServerCoreType.Fabric
                        Dim Json As JArray = GetStationJson(
                            $"https://meta.fabricmc.net/v2/versions/loader/{StringUtils.UrlEscape(Version)}")
                        For Each Entry As JObject In Json
                            Dim Loader = CType(Entry("loader"), JObject)
                            Builds.Add(($"Loader {Loader("version")}{If(Loader("stable")?.ToObject(Of Boolean), "", "（测试版）")}", Loader))
                        Next
                    Case ServerCoreType.Forge
                        Dim ForgeLoader As New LoaderTask(Of String, List(Of DlForgeVersionEntry))(
                            "Forge 服务端版本列表", AddressOf DlForgeVersionMain)
                        ForgeLoader.WaitForExit(Version)
                        For Each Entry In ForgeLoader.Output.
                            Where(Function(item) item.Category = "installer").
                            OrderByDescending(Function(item) item)
                            Builds.Add(($"Forge {Entry.FileVersion}{If(Entry.IsRecommended, "（推荐）", "")}", Entry))
                        Next
                    Case ServerCoreType.NeoForge
                        EnsureNeoForgeVersionsLoaded()
                        For Each Entry In DlNeoForgeListLoader.Output.Value.
                            Where(Function(item) String.Equals(item.Inherit, Version, StringComparison.OrdinalIgnoreCase)).
                            OrderByDescending(Function(item) item)
                            Builds.Add(($"NeoForge {Entry.ApiName}{If(Entry.IsBeta, "（测试版）", "")}", Entry))
                        Next
                    Case ServerCoreType.MSLMC
                        Dim CoreName As String = CType(VersionOption, ServerVersionOption).Metadata.ToString
                        Dim MslmcBuilds = GetMslmcBuilds(CoreName, Version)
                        BuildTotal = MslmcBuilds.Count
                        For Each Entry In MslmcBuilds
                            Builds.Add(($"构建 {Entry.BuildName}", Entry))
                        Next
                End Select
                RunInUi(
                Sub()
                    If CurrentLoadId <> BuildLoadId Then Return
                    ComboServerBuild.Items.Clear()
                    For Each Build In Builds
                        ComboServerBuild.Items.Add(New MyComboBoxItem With {.Content = Build.Name, .Tag = Build.Tag})
                    Next
                    If ComboServerBuild.Items.Count > 0 Then
                        ComboServerBuild.SelectedIndex = 0
                        BtnDownloadServerCore.IsEnabled = True
                        HintServerDownload.Text =
                            $"已获取 {ComboServerBuild.Items.Count} 个构建版本" &
                            If(BuildTotal > ComboServerBuild.Items.Count,
                               $"（接口共 {BuildTotal} 个构建）。", "。")
                        UpdateSuggestedServerFolderName()
                    Else
                        HintServerDownload.Theme = MyHint.Themes.Yellow
                        HintServerDownload.Text = "该版本没有可用构建。"
                    End If
                    If IsMslmcBuild Then
                        IsMslmcBuildLoading = False
                        ComboMslmcCore.IsEnabled = True
                        ComboServerVersion.IsEnabled = True
                        BtnRefreshServerCore.IsEnabled = True
                    End If
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    If CurrentLoadId <> BuildLoadId Then Return
                    If IsMslmcBuild Then
                        IsMslmcBuildLoading = False
                        ComboMslmcCore.IsEnabled = True
                        ComboServerVersion.IsEnabled = True
                        BtnRefreshServerCore.IsEnabled = True
                    End If
                    HintServerDownload.Theme = MyHint.Themes.Red
                    HintServerDownload.Text = "获取构建列表失败：" & ex.Message
                End Sub)
            End Try
        End Sub, "Load Server Core Builds")
    End Sub

    Private Sub BtnDownloadServerCore_Click(sender As Object, e As EventArgs) Handles BtnDownloadServerCore.Click
        If ComboServerVersion.SelectedItem Is Nothing OrElse ComboServerBuild.SelectedItem Is Nothing Then
            Hint("请先选择 Minecraft 版本和构建版本。", HintType.Red)
            Return
        End If
        Dim FolderName As String = TextServerFolderName.Text.Trim
        If FolderName = "" Then
            FolderName = GetSuggestedServerFolderName()
            TextServerFolderName.Text = FolderName
        End If
        Dim CreatedFolderForCleanup As Boolean = False
        Dim ServerFolder As String = Nothing
        Try
            ServerFolder = GetServerFolder(FolderName)
            If DirectoryUtils.Exists(ServerFolder) AndAlso Not DirectoryUtils.IsEmpty(ServerFolder) Then
                Hint("该服务器文件夹已经存在且不为空，请更换名称。", HintType.Red)
                Return
            End If
            DirectoryUtils.Create(ServerFolder)
            CreatedFolderForCleanup = True
            BtnDownloadServerCore.IsEnabled = False
            BtnRefreshServerCore.IsEnabled = False
            BtnCancelServerCore.Visibility = Visibility.Visible
            BtnCancelServerCore.IsEnabled = True
            BtnDownloadServerCore.Text = "准备下载……"
            Dim CoreType As ServerCoreType = CType(Val(ComboServerCoreType.SelectedItem.Tag), ServerCoreType)
            Dim VersionOption As ServerVersionOption = CType(ComboServerVersion.SelectedItem.Tag, ServerVersionOption)
            Dim Version As String = VersionOption.MinecraftVersion
            Dim BuildTag As Object = ComboServerBuild.SelectedItem.Tag

            Dim DownloadFiles As New LoaderDownload("下载服务端核心", New List(Of NetFile))
            Dim DownloadTask As New LoaderTask(Of Integer, ServerDownloadInfo)("获取服务端核心下载地址",
                Sub(Task)
                    '这些状态必须在任务体内完成：OnStateChanged 由 RunInUi 异步触发，
                    '而 LoaderCombo 可能已在工作线程启动下一个加载器，此时下载列表仍是空的，
                    '下载会被直接跳过，最后报“服务端安装器不存在”。
                    Dim Info = GetServerDownloadInfo(CoreType, Version, VersionOption.Metadata, BuildTag)
                    Dim Target As String
                    If Info.IsInstaller Then
                        Target = Path.Combine(ServerFolder, "PCL\Installers", Info.FileName)
                    Else
                        Target = Path.Combine(ServerFolder, Info.FileName)
                    End If
                    Info.LocalPath = Target
                    Dim Sources = {Info.Url, Info.FallbackUrl}.Where(Function(url) Not String.IsNullOrWhiteSpace(url)).ToArray
                    Dim Checker As New FileChecker With {.ActualSize = Info.Size}
                    If Info.IsInstaller Then Checker.MinSize = 64 * 1024
                    If Not String.IsNullOrWhiteSpace(Info.Hash) Then Checker.Hash = Info.Hash
                    DownloadFiles.Files = New ConcurrentQueue(Of NetFile)(
                        New List(Of NetFile) From {
                            New NetFile(Sources, Target, Checker)
                        })
                    Task.Output = Info
                End Sub)
            Dim InstallTask As New LoaderTask(Of Integer, ServerLaunchInfo)("安装服务端核心",
                Sub(Task)
                    Dim Info = DownloadTask.Output
                    If Info.IsInstaller Then
                        Task.Output = InstallServerCore(ServerFolder, Info.LocalPath,
                            If(FindRecommendedJava(Version), "java.exe"),
                            CoreType, Version, Info.LoaderVersion, Task.CreateCancellationToken)
                    Else
                        Task.Output = FindServerLaunchInfo(ServerFolder, CoreType)
                    End If
                End Sub)
            Dim DownloadCombo As New LoaderCombo(Of Integer)("服务器核心创建",
                New List(Of LoaderBase) From {DownloadTask, DownloadFiles, InstallTask}) With {
                .OnStateChanged =
                Sub(Loader)
                    Select Case Loader.State
                        Case LoadState.Loading
                            RunInUi(Sub()
                                        BtnDownloadServerCore.Text =
                                            If(InstallTask.State = LoadState.Loading, "正在安装服务端……",
                                               $"{Math.Round(Loader.Progress * 100)}%")
                                    End Sub)
                        Case LoadState.Finished
                            Try
                                CreateServerConfigAfterDownload(ServerFolder, DownloadTask.Output,
                                    InstallTask.Output, CoreType, Version)
                                CreatedFolderForCleanup = False
                                RunInUi(
                                Sub()
                                    BtnDownloadServerCore.Text = "下载并创建服务器"
                                    BtnDownloadServerCore.IsEnabled = True
                                    BtnRefreshServerCore.IsEnabled = True
                                    BtnCancelServerCore.Visibility = Visibility.Collapsed
                                    ActiveServerDownload = Nothing
                                    HintServerDownload.Theme = MyHint.Themes.Blue
                                    HintServerDownload.Text = "服务器创建完成，请回到启动页选择服务器启动与管理。"
                                    If MyMsgBox("服务器核心下载完成，是否打开服务器文件夹？", "创建完成", "打开", "关闭") = 1 Then OpenExplorer(ServerFolder)
                                End Sub)
                            Catch ex As Exception
                                Try
                                    If DirectoryUtils.Exists(ServerFolder) Then DeleteCreatedServerFolder(ServerFolder)
                                    CreatedFolderForCleanup = False
                                Catch cleanEx As Exception
                                    Logger.Warn(cleanEx, "清理创建失败的服务端目录失败")
                                End Try
                                Logger.Error(ex, "创建服务器配置失败", LogBehavior.Alert)
                                RunInUi(
                                Sub()
                                    BtnDownloadServerCore.Text = "下载并创建服务器"
                                    BtnDownloadServerCore.IsEnabled = True
                                    BtnRefreshServerCore.IsEnabled = True
                                    BtnCancelServerCore.Visibility = Visibility.Collapsed
                                    ActiveServerDownload = Nothing
                                End Sub)
                            End Try
                        Case LoadState.Failed, LoadState.Canceled
                            Try
                                If CreatedFolderForCleanup AndAlso DirectoryUtils.Exists(ServerFolder) Then
                                    DeleteCreatedServerFolder(ServerFolder)
                                End If
                                CreatedFolderForCleanup = False
                            Catch cleanEx As Exception
                                Logger.Warn(cleanEx, "清理未完成的服务端目录失败")
                            End Try
                            RunInUi(
                            Sub()
                                BtnDownloadServerCore.Text = "下载并创建服务器"
                                BtnDownloadServerCore.IsEnabled = True
                                BtnRefreshServerCore.IsEnabled = True
                                BtnCancelServerCore.Visibility = Visibility.Collapsed
                                ActiveServerDownload = Nothing
                                HintServerDownload.Theme = MyHint.Themes.Red
                                HintServerDownload.Text = "服务器核心下载失败：" & If(Loader.Error?.Message, "已取消")
                            End Sub)
                    End Select
                End Sub}
            ActiveServerDownload = DownloadCombo
            DownloadCombo.Start(1)
            LoaderTaskbarAdd(DownloadCombo)
            FrmMain.BtnExtraDownload.ShowRefresh()
        Catch ex As Exception
            Try
                If CreatedFolderForCleanup AndAlso DirectoryUtils.Exists(ServerFolder) Then DeleteCreatedServerFolder(ServerFolder)
            Catch cleanEx As Exception
                Logger.Warn(cleanEx, "清理未完成的服务端目录失败")
            End Try
            Logger.Error(ex, "创建服务器失败", LogBehavior.Alert)
            BtnDownloadServerCore.Text = "下载并创建服务器"
            BtnDownloadServerCore.IsEnabled = True
            BtnRefreshServerCore.IsEnabled = True
            BtnCancelServerCore.Visibility = Visibility.Collapsed
            ActiveServerDownload = Nothing
        End Try
    End Sub

    Private Sub BtnCancelServerCore_Click(sender As Object, e As EventArgs) Handles BtnCancelServerCore.Click
        If ActiveServerDownload Is Nothing Then Return
        BtnCancelServerCore.IsEnabled = False
        BtnDownloadServerCore.Text = "正在取消……"
        ActiveServerDownload.Cancel()
    End Sub

    Private Sub TextServerFolderName_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextServerFolderName.TextChanged
        If IsUpdatingServerFolderName Then Return
        IsServerFolderNameCustom = TextServerFolderName.Text.Trim <> LastSuggestedFolderName
    End Sub

    Private Function GetSuggestedServerFolderName() As String
        Dim CoreName As String = If(ComboServerCoreType.SelectedItem?.Content?.ToString, "Server")
        CoreName = CoreName.Replace("原版服务器（Vanilla）", "Vanilla")
        If CType(Val(ComboServerCoreType.SelectedItem?.Tag), ServerCoreType) = ServerCoreType.MSLMC AndAlso
           ComboMslmcCore.SelectedItem IsNot Nothing Then
            CoreName = ComboMslmcCore.SelectedItem.Tag.ToString
        End If
        Dim VersionName As String = If(ComboServerVersion.SelectedItem?.Content?.ToString, "")
        Dim BaseName As String = (CoreName & "-" & VersionName).TrimEnd("-"c)
        Return GetUniqueServerFolderName(BaseName)
    End Function

    Private Function GetUniqueServerFolderName(BaseName As String) As String
        Dim Candidate As String = BaseName
        Dim Index As Integer = 2
        Do While DirectoryUtils.Exists(Path.Combine(ServerRoot, Candidate))
            Candidate = $"{BaseName}-{Index}"
            Index += 1
        Loop
        Return Candidate
    End Function

    Private Sub UpdateSuggestedServerFolderName()
        If IsServerFolderNameCustom Then Return
        Dim Suggested As String = GetSuggestedServerFolderName()
        LastSuggestedFolderName = Suggested
        IsUpdatingServerFolderName = True
        TextServerFolderName.Text = Suggested
        IsUpdatingServerFolderName = False
    End Sub

    Private Function GetServerDownloadInfo(CoreType As ServerCoreType, Version As String,
                                           VersionTag As Object, BuildTag As Object) As ServerDownloadInfo
        Select Case CoreType
            Case ServerCoreType.Vanilla
                Dim Entry As JObject = CType(VersionTag, JObject)
                Dim VersionJson As JObject = NetRequestByClientRetry(Entry("url").ToString,
                    Accept:="application/json", RequireJson:=True).DeserializeJson()
                Dim Server = VersionJson("downloads")("server")
                If Server Is Nothing OrElse Server("url") Is Nothing Then Throw New Exception("该 Minecraft 版本没有官方服务端。")
                Return New ServerDownloadInfo With {
                    .Url = Server("url").ToString,
                    .FileName = "server.jar",
                    .Hash = Server("sha1")?.ToString,
                    .Size = If(Server("size") Is Nothing, -1L, Server("size").ToObject(Of Long)),
                    .CoreType = CoreType
                }
            Case ServerCoreType.Paper
                Dim Build As JObject = CType(BuildTag, JObject)
                Dim Download = Build("downloads")("server:default")
                Return New ServerDownloadInfo With {
                    .Url = Download("url").ToString,
                    .FileName = Download("name").ToString,
                    .Hash = Download("checksums")("sha256").ToString,
                    .Size = Download("size").ToObject(Of Long),
                    .CoreType = CoreType
                }
            Case ServerCoreType.Purpur
                Dim Build As String = BuildTag.ToString
                Dim Meta As JObject = GetStationJson(
                    $"https://api.purpurmc.org/v2/purpur/{StringUtils.UrlEscape(Version)}/{StringUtils.UrlEscape(Build)}")
                Return New ServerDownloadInfo With {
                    .Url = $"https://api.purpurmc.org/v2/purpur/{StringUtils.UrlEscape(Version)}/{StringUtils.UrlEscape(Build)}/download",
                    .FileName = $"purpur-{Version}-{Build}.jar",
                    .Hash = Meta("md5")?.ToString,
                    .Size = -1,
                    .CoreType = CoreType
                }
            Case ServerCoreType.Fabric
                Dim Installers As JArray = GetStationJson("https://meta.fabricmc.net/v2/versions/installer")
                Dim Installer As JObject = Installers.OfType(Of JObject)().
                    FirstOrDefault(Function(entry) entry("stable")?.ToObject(Of Boolean) = True)
                If Installer Is Nothing Then Installer = Installers.OfType(Of JObject)().FirstOrDefault
                If Installer Is Nothing Then Throw New Exception("Fabric 没有返回可用的安装器。")
                Return New ServerDownloadInfo With {
                    .Url = Installer("url").ToString,
                    .FileName = PathUtils.GetLastPart(Installer("url").ToString),
                    .Size = -1,
                    .IsInstaller = True,
                    .CoreType = CoreType,
                    .LoaderVersion = CType(BuildTag, JObject)("version").ToString
                }
            Case ServerCoreType.Forge
                Dim Build As DlForgeVersionEntry = CType(BuildTag, DlForgeVersionEntry)
                Dim FileName As String =
                    $"forge-{Build.Inherit}-{Build.FileVersion}-installer.jar"
                Dim PathPart As String =
                    $"net/minecraftforge/forge/{Build.Inherit}-{Build.FileVersion}/{FileName}"
                Return New ServerDownloadInfo With {
                    .Url = "https://bmclapi2.bangbang93.com/maven/" & PathPart,
                    .FallbackUrl = "https://files.minecraftforge.net/maven/" & PathPart,
                    .FileName = FileName,
                    .Hash = Build.Hash,
                    .Size = -1,
                    .IsInstaller = True,
                    .CoreType = CoreType,
                    .LoaderVersion = Build.FileVersion
                }
            Case ServerCoreType.NeoForge
                Dim Build As DlNeoForgeListEntry = CType(BuildTag, DlNeoForgeListEntry)
                Dim FileName As String = $"neoforge-{Build.ApiName}-installer.jar"
                Return New ServerDownloadInfo With {
                    .Url = Build.UrlBase.Replace("maven.neoforged.net/releases", "bmclapi2.bangbang93.com/maven") & "-installer.jar",
                    .FallbackUrl = Build.UrlBase & "-installer.jar",
                    .FileName = FileName,
                    .Size = -1,
                    .IsInstaller = True,
                    .CoreType = CoreType,
                    .LoaderVersion = Build.ApiName
                }
            Case ServerCoreType.MSLMC
                Dim Build As MslmcBuildEntry = CType(BuildTag, MslmcBuildEntry)
                'VersionTag 传入的是 ServerVersionOption.Metadata，对 MSLMC 而言就是核心名（String），
                '不能再强制转换为 ServerVersionOption，否则必然抛 InvalidCastException。
                Dim CoreName As String = If(VersionTag?.ToString, "")
                If String.IsNullOrWhiteSpace(CoreName) Then Throw New Exception("没有获取到所选 MSLMC 核心的名称，请重新选择核心。")
                Dim Download = GetMslmcDownload(CoreName, Build.MinecraftVersion, Build.BuildName)
                Return New ServerDownloadInfo With {
                    .Url = Download.DownloadUrl,
                    .FileName = Download.FileName,
                    .Hash = Download.Sha256,
                    .Size = -1,
                    .CoreType = CoreType,
                    .LoaderVersion = Build.BuildName,
                    .MslmcCore = CoreName
                }
            Case Else
                Throw New ArgumentOutOfRangeException(NameOf(CoreType))
        End Select
    End Function

    Private Sub CreateServerConfigAfterDownload(ServerFolder As String, DownloadInfo As ServerDownloadInfo,
                                                LaunchInfo As ServerLaunchInfo,
                                                CoreType As ServerCoreType, Version As String)
        Dim StoredVersion As String = Version
        Dim DisplayName As String = $"{CoreType} {Version}"
        If CoreType = ServerCoreType.MSLMC AndAlso Not String.IsNullOrWhiteSpace(DownloadInfo.MslmcCore) Then
            DisplayName = $"{DownloadInfo.MslmcCore} {Version}"
        End If
        Dim Config As New JObject From {
            {"Name", DisplayName},
            {"CoreType", CoreType.ToString},
            {"MinecraftVersion", StoredVersion},
            {"LaunchMode", LaunchInfo.LaunchMode},
            {"CoreJar", If(LaunchInfo.CoreJar, "")},
            {"LaunchArgsFile", If(LaunchInfo.LaunchArgsFile, "")},
            {"JavaPath", If(FindRecommendedJava(StoredVersion), "java.exe")},
            {"XmsMB", 1024}, {"XmxMB", 4096}, {"Port", 25565},
            {"NoGui", True},
            {"JvmArgs", "-XX:+UseG1GC -Dfile.encoding=UTF-8"},
            {"ServerArgs", ""},
            {"SupportsPlugins", CoreType = ServerCoreType.Paper OrElse CoreType = ServerCoreType.Purpur},
            {"SupportsMods", CoreType = ServerCoreType.Fabric OrElse CoreType = ServerCoreType.Forge OrElse
                              CoreType = ServerCoreType.NeoForge}
        }
        If CoreType = ServerCoreType.MSLMC AndAlso Not String.IsNullOrWhiteSpace(DownloadInfo.MslmcCore) Then
            Config("MslmcCore") = DownloadInfo.MslmcCore
            Config("SupportsPlugins") = SupportsPluginsForCore(DownloadInfo.MslmcCore)
            Config("SupportsMods") = SupportsModsForCore(DownloadInfo.MslmcCore)
        End If
        SaveServerConfig(ServerFolder, Config)
    End Sub

    Private Sub EnsureNeoForgeVersionsLoaded()
        If DlNeoForgeListLoader.State = LoadState.Finished Then Return
        If DlNeoForgeListLoader.State = LoadState.Loading Then
            DlNeoForgeListLoader.WaitForExit()
        Else
            DlNeoForgeListLoader.WaitForExit(IsForceRestart:=True)
        End If
    End Sub

    ''' <summary>
    ''' 解析 Forge 的版本推荐信息。
    ''' 兼容官方 promotions_slim.json（对象：{"promos": {"1.21.1-latest": "52.1.20"}}）
    ''' 与 BMCLAPI /forge/promos（数组：[{"name": "1.21.1-latest", "build": {"mcversion": "1.21.1", "version": "52.1.20"}}]）。
    ''' </summary>
    Private Function ParseForgePromotions(Json As JToken) As List(Of (MinecraftVersion As String, IsRecommended As Boolean, Version As String))
        Dim Result As New List(Of (MinecraftVersion As String, IsRecommended As Boolean, Version As String))
        If Json Is Nothing Then Throw New Exception("Forge 没有返回版本信息。")
        Select Case Json.Type
            Case JTokenType.Object
                Dim Promos As JObject = TryCast(Json("promos"), JObject)
                If Promos Is Nothing OrElse Promos.Count = 0 Then Throw New Exception("Forge 版本信息缺少 promos 字段。")
                For Each Promotion As JProperty In Promos.Properties
                    Dim MinecraftVersion As String = Nothing
                    Dim IsRecommended As Boolean = False
                    If Not TryParseForgePromotionName(Promotion.Name, MinecraftVersion, IsRecommended) Then Continue For
                    Result.Add((MinecraftVersion, IsRecommended, Promotion.Value.ToString))
                Next
            Case JTokenType.Array
                For Each Token As JObject In Json.OfType(Of JObject)()
                    Dim Build As JObject = TryCast(Token("build"), JObject)
                    If Build Is Nothing Then Continue For
                    Dim Version As String = If(Build("version") Is Nothing, "", Build("version").ToString)
                    If String.IsNullOrWhiteSpace(Version) Then Continue For
                    Dim Name As String = If(Token("name") Is Nothing, "", Token("name").ToString)
                    Dim MinecraftVersion As String = If(Build("mcversion") Is Nothing, "", Build("mcversion").ToString)
                    Dim IsRecommended As Boolean = False
                    Dim NameVersion As String = Nothing
                    Dim NameRecommended As Boolean = False
                    If TryParseForgePromotionName(Name, NameVersion, NameRecommended) Then
                        IsRecommended = NameRecommended
                        If String.IsNullOrWhiteSpace(MinecraftVersion) Then MinecraftVersion = NameVersion
                    Else
                        'BMCLAPI 里最新版本的 name 可能只有 latest / recommended，MC 版本需要从 build.mcversion 取
                        IsRecommended = Name.Equals("recommended", StringComparison.OrdinalIgnoreCase)
                    End If
                    If String.IsNullOrWhiteSpace(MinecraftVersion) Then Continue For
                    Result.Add((MinecraftVersion, IsRecommended, Version))
                Next
            Case Else
                Throw New Exception("Forge 返回了无法识别的版本信息格式。")
        End Select
        If Not Result.Any() Then Throw New Exception("Forge 没有返回可用的版本信息。")
        Return Result
    End Function

    ''' <summary>
    ''' 从 "1.21.1-latest" / "1.21.1-recommended" 这样的名字中拆出 MC 版本和是否为推荐版。
    ''' </summary>
    Private Function TryParseForgePromotionName(Name As String, ByRef MinecraftVersion As String,
                                                ByRef IsRecommended As Boolean) As Boolean
        MinecraftVersion = Nothing
        IsRecommended = False
        If String.IsNullOrWhiteSpace(Name) Then Return False
        Const RecommendedSuffix As String = "-recommended"
        Const LatestSuffix As String = "-latest"
        If Name.EndsWith(RecommendedSuffix, StringComparison.OrdinalIgnoreCase) Then
            IsRecommended = True
            MinecraftVersion = Name.Substring(0, Name.Length - RecommendedSuffix.Length)
        ElseIf Name.EndsWith(LatestSuffix, StringComparison.OrdinalIgnoreCase) Then
            MinecraftVersion = Name.Substring(0, Name.Length - LatestSuffix.Length)
        Else
            Return False
        End If
        Return Not String.IsNullOrWhiteSpace(MinecraftVersion)
    End Function

    Private Function GetStationJson(Url As String) As JToken
        Return NetRequestByClientRetry(Url, Headers:=Headers, RequireJson:=True).DeserializeJson()
    End Function

End Class
