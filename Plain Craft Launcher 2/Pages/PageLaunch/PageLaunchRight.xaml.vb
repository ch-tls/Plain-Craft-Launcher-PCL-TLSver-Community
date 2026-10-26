Imports System.Windows.Threading

Public Class PageLaunchRight
    Implements IRefreshable, IDispatcherUnhandledException

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        PanLog.Visibility = ModeDebug.ToVisibility
        '快照版提示
        PanHint.Visibility =
            (BuildType <> BuildTypes.Release AndAlso VersionBranchMain = "Official" AndAlso
             CurrentRank < DonationRank.Rank23 AndAlso Not Settings.Get(Of Boolean)("HintSnapshot")).ToVisibility
    End Sub

#Region "服务器启动与简单管理"

    Private _SelectedServerFolder As String = Nothing
    Public ReadOnly Property SelectedServerFolder As String
        Get
            Return _SelectedServerFolder
        End Get
    End Property

    Public Sub SetServerMode(Enabled As Boolean)
        PanMain.Visibility = If(Enabled, Visibility.Collapsed, Visibility.Visible)
        PanServerManager.Visibility = If(Enabled, Visibility.Visible, Visibility.Collapsed)
        If Enabled Then PanBack.ScrollToHome()
    End Sub

    Public Sub SelectServer(Folder As String)
        If String.IsNullOrWhiteSpace(Folder) OrElse Not DirectoryUtils.Exists(Folder) Then
            ClearServer()
            Return
        End If
        _SelectedServerFolder = Folder
        Dim Config = LoadServerConfig(Folder)
        TextServerLaunchName.Text = Config("Name").ToString
        LabServerLaunchFolder.Text = Folder
        LabServerLaunchFolder.ToolTip = Folder
        TextServerLaunchVersion.Text = Config("MinecraftVersion")?.ToString
        Dim CoreDisplay As String = GetConfiguredCoreName(Config)
        LabServerLaunchCore.Text = $"{CoreDisplay}  ·  {Config("LaunchMode")}"
        TextServerLaunchXms.Text = Config("XmsMB").ToString
        TextServerLaunchXmx.Text = Config("XmxMB").ToString
        TextServerLaunchPort.Text = Config("Port").ToString
        CheckServerLaunchNoGui.Checked = Config("NoGui").ToObject(Of Boolean)
        TextServerLaunchJvmArgs.Text = Config("JvmArgs").ToString
        TextServerLaunchArgs.Text = Config("ServerArgs").ToString
        RefreshServerJavaList(Config("JavaPath").ToString)
        SelectServerContentFolder(GetServerContentFolder(Folder, Config))
        RefreshServerEula()
        RefreshServerContent()
    End Sub

    Public Sub ClearServer()
        _SelectedServerFolder = Nothing
        TextServerLaunchName.Text = ""
        LabServerLaunchFolder.Text = "尚未选择服务器"
        LabServerLaunchCore.Text = ""
        TextServerLaunchVersion.Text = ""
        TextServerLaunchXms.Text = ""
        TextServerLaunchXmx.Text = ""
        TextServerLaunchPort.Text = ""
        TextServerLaunchJvmArgs.Text = ""
        TextServerLaunchArgs.Text = ""
        ComboServerLaunchJava.Items.Clear()
        ComboServerLaunchJava.Text = ""
        LabServerJavaHint.Text = "请先在启动页左侧选择一个服务器。"
        PanServerContentFiles.Children.Clear()
        LabServerLaunchEula.Text = "请先在启动页左侧选择一个服务器。"
        BtnAcceptServerLaunchEula.IsEnabled = False
    End Sub

    Private Sub RefreshServerJavaList(Preferred As String)
        ComboServerLaunchJava.Items.Clear()
        For Each JavaEntry As Java In Configs.JavaList.Get()
            ComboServerLaunchJava.Items.Add(New MyComboBoxItem With {
                .Content = $"Java {If(JavaEntry.Version Is Nothing, "未检测", JavaEntry.Version.ToString())} - {JavaEntry.JavaExePath}",
                .Tag = JavaEntry.JavaExePath
            })
        Next
        ComboServerLaunchJava.Text = If(String.IsNullOrWhiteSpace(Preferred),
                                        If(FindJavaFromEnvironment(), "java.exe"), Preferred)
        UpdateServerJavaHint()
    End Sub

    ''' <summary>
    ''' 刷新服务器 Java 提示：当前 MC 版本推荐的主版本，以及已扫描到的匹配数量。
    ''' </summary>
    Private Sub UpdateServerJavaHint()
        If LabServerJavaHint Is Nothing Then Return
        Dim McVersion As String = If(TextServerLaunchVersion?.Text, "").Trim
        Try
            Dim Javas = Configs.JavaList.Get()
            If String.IsNullOrWhiteSpace(McVersion) Then
                LabServerJavaHint.Text = $"已扫描到 {Javas.Count} 个 Java。填写 Minecraft 版本后会显示推荐的主版本。"
                Return
            End If
            Dim RecommendedMajor As Integer = GetRecommendedJavaMajor(McVersion)
            Dim Matched As Integer = Javas.Where(Function(java) java.Version IsNot Nothing AndAlso java.Version.Major = RecommendedMajor).Count()
            If Matched > 0 Then
                LabServerJavaHint.Text = $"Minecraft {McVersion} 推荐 Java {RecommendedMajor}，已扫描到 {Matched} 个匹配的 Java（共 {Javas.Count} 个）。"
            Else
                LabServerJavaHint.Text = $"Minecraft {McVersion} 推荐 Java {RecommendedMajor}，但没有扫描到匹配的 Java。请点击「扫描 Java」，或手动选择 java.exe。"
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新服务器 Java 提示失败")
        End Try
    End Sub

    ''' <summary>
    ''' 重新扫描本机 Java（与启动页的 Java 列表共用同一套扫描逻辑和结果）。
    ''' </summary>
    Private Sub BtnScanServerJava_Click(sender As Object, e As EventArgs) Handles BtnScanServerJava.Click
        Dim KeepJava As String = ComboServerLaunchJava.Text
        BtnScanServerJava.IsEnabled = False
        Hint("正在扫描本机 Java，请稍候……", Log:=False)
        RunInNewThread(
        Sub()
            Try
                '与 ManuallySearchJava 一致：先重置检查状态，再强制重扫并等待结束
                Configs.JavaList.Get().ForAll(Sub(java) java.InvalidateChecked())
                JavaListRefreshWorker.Start()
                JavaListRefreshWorker.WaitIfRunningAsync().Run()
                Dim Found As Integer = Configs.JavaList.Get().Count
                RunInUi(
                Sub()
                    Try
                        RefreshServerJavaList(KeepJava)
                        '当前选择无效时，自动改成该版本推荐的 Java
                        If String.IsNullOrWhiteSpace(KeepJava) OrElse Not FileUtils.Exists(KeepJava) Then
                            Dim Recommended As String = FindRecommendedJava(TextServerLaunchVersion.Text.Trim)
                            If Not String.IsNullOrWhiteSpace(Recommended) Then ComboServerLaunchJava.Text = Recommended
                        End If
                    Finally
                        BtnScanServerJava.IsEnabled = True
                    End Try
                End Sub)
                If Found > 0 Then
                    RunInUi(Sub() Hint($"扫描完成，共找到 {Found} 个 Java。", HintType.Green))
                Else
                    RunInUi(Sub() Hint("未找到可用的 Java，请手动选择 java.exe。", HintType.Red))
                End If
            Catch ex As Exception
                Logger.Error(ex, "扫描服务器 Java 失败")
                RunInUi(Sub() BtnScanServerJava.IsEnabled = True)
            End Try
        End Sub, "Scan Server Java")
    End Sub

    Private Sub TextServerLaunchVersion_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextServerLaunchVersion.TextChanged
        UpdateServerJavaHint()
    End Sub

    Private Sub SelectServerContentFolder(FolderName As String)
        Dim Target = ComboServerContentFolder.Items.Cast(Of MyComboBoxItem)().
            FirstOrDefault(Function(item) item.Tag?.ToString = FolderName)
        ComboServerContentFolder.SelectedItem = If(Target, ComboServerContentFolder.Items(0))
    End Sub

    Private Function GetSelectedServerContentFolder() As String
        If SelectedServerFolder Is Nothing Then Return Nothing
        Dim FolderName As String = If(ComboServerContentFolder.SelectedItem?.Tag?.ToString, "mods")
        Return Path.Combine(SelectedServerFolder, FolderName)
    End Function

    Public Function SaveServerSettings(ShowHint As Boolean) As Boolean
        If SelectedServerFolder Is Nothing Then Return False
        Dim Xms As Integer, Xmx As Integer, Port As Integer
        If Not Integer.TryParse(TextServerLaunchXms.Text, Xms) OrElse Xms < 128 Then
            Hint("最小内存必须是大于等于 128 的整数。", HintType.Red)
            Return False
        End If
        If Not Integer.TryParse(TextServerLaunchXmx.Text, Xmx) OrElse Xmx < Xms Then
            Hint("最大内存必须是整数，且不能小于最小内存。", HintType.Red)
            Return False
        End If
        If Not Integer.TryParse(TextServerLaunchPort.Text, Port) OrElse Port < 1 OrElse Port > 65535 Then
            Hint("端口必须在 1 到 65535 之间。", HintType.Red)
            Return False
        End If
        Dim JavaPath As String
        Try
            JavaPath = ResolveServerJavaPath(SelectedServerFolder, ComboServerLaunchJava.Text)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
            Return False
        End Try
        Dim OldConfig = LoadServerConfig(SelectedServerFolder)
        Dim LaunchMode As String = If(OldConfig("LaunchMode")?.ToString, "Jar")
        Dim LaunchArgsFile As String = If(OldConfig("LaunchArgsFile")?.ToString, "")
        If LaunchMode = "Args" AndAlso
           (String.IsNullOrWhiteSpace(LaunchArgsFile) OrElse Not FileUtils.Exists(Path.Combine(SelectedServerFolder, LaunchArgsFile))) Then
            Hint("未找到 Forge/NeoForge 启动参数文件，请重新安装服务器。", HintType.Red)
            Return False
        End If
        Dim Config As New JObject From {
            {"Name", If(String.IsNullOrWhiteSpace(TextServerLaunchName.Text), PathUtils.GetLastPart(SelectedServerFolder), TextServerLaunchName.Text.Trim)},
            {"CoreType", InferServerCoreType(SelectedServerFolder, OldConfig).ToString},
            {"MinecraftVersion", TextServerLaunchVersion.Text.Trim},
            {"MslmcCore", If(OldConfig("MslmcCore")?.ToString, "")},
            {"SupportsPlugins", SupportsServerPlugins(SelectedServerFolder, OldConfig)},
            {"SupportsMods", SupportsServerMods(SelectedServerFolder, OldConfig)},
            {"LaunchMode", LaunchMode},
            {"CoreJar", If(OldConfig("CoreJar")?.ToString, "")},
            {"LaunchArgsFile", LaunchArgsFile},
            {"JavaPath", JavaPath},
            {"XmsMB", Xms}, {"XmxMB", Xmx}, {"Port", Port},
            {"NoGui", CheckServerLaunchNoGui.Checked},
            {"JvmArgs", TextServerLaunchJvmArgs.Text.Trim},
            {"ServerArgs", TextServerLaunchArgs.Text.Trim}
        }
        SaveServerConfig(SelectedServerFolder, Config)
        If ShowHint Then Hint("服务器设置已保存。", HintType.Green)
        FrmLaunchLeft.RefreshServerList()
        Return True
    End Function

    Private Sub RefreshServerEula()
        If SelectedServerFolder Is Nothing Then Return
        If Not FileUtils.Exists(GetEulaPath(SelectedServerFolder)) Then
            LabServerLaunchEula.Text = "尚未生成 eula.txt。请先在终端中启动一次服务器。"
            BtnAcceptServerLaunchEula.IsEnabled = False
        ElseIf IsEulaAccepted(SelectedServerFolder) Then
            LabServerLaunchEula.Text = "EULA 已由用户确认。"
            BtnAcceptServerLaunchEula.IsEnabled = False
        Else
            LabServerLaunchEula.Text = "已检测到 eula.txt，但尚未同意。请阅读 EULA 后自行确认。"
            BtnAcceptServerLaunchEula.IsEnabled = True
        End If
    End Sub

    Public Sub RefreshServerContent()
        If SelectedServerFolder Is Nothing Then Return
        PanServerContentFiles.Children.Clear()
        Dim ContentFolder = GetSelectedServerContentFolder()
        If Not DirectoryUtils.Exists(ContentFolder) Then
            DirectoryUtils.Create(ContentFolder)
        End If
        Dim Files = DirectoryUtils.EnumerateFiles(ContentFolder, False).
            Where(Function(path)
                      Dim Name = PathUtils.GetLastPart(path).ToLowerInvariant
                      Return Name.EndsWith(".jar") OrElse Name.EndsWith(".jar.disabled")
                  End Function).
            OrderBy(Function(path) PathUtils.GetLastPart(path), StringComparer.OrdinalIgnoreCase).
            ToList
        If Not Files.Any() Then
            PanServerContentFiles.Children.Add(New TextBlock With {
                .Text = "当前目录没有 Mod 或插件文件。",
                .Opacity = 0.6,
                .Margin = New Thickness(2, 4, 0, 4)
            })
            Return
        End If
        For Each FilePath As String In Files
            Dim CurrentPath As String = FilePath
            Dim FileName As String = PathUtils.GetLastPart(CurrentPath)
            Dim IsDisabled As Boolean = FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            Dim Item As New MyListItem With {
                .Title = FileName,
                .Info = If(IsDisabled, "已禁用", "已启用"),
                .Height = 42,
                .MinPaddingRight = 35,
                .Tag = CurrentPath
            }
            Dim ToggleButton As New MyIconButton With {
                .LogoScale = 0.95,
                .Logo = If(IsDisabled, Logo.IconButtonCheck, Logo.IconButtonStop),
                .ToolTip = If(IsDisabled, "启用", "禁用")
            }
            AddHandler ToggleButton.Click,
                Sub()
                    ToggleServerContent(CurrentPath)
                End Sub
            Dim DeleteButton As New MyIconButton With {
                .LogoScale = 0.95,
                .Logo = Logo.IconButtonDelete,
                .ToolTip = "删除"
            }
            AddHandler DeleteButton.Click,
                Sub()
                    DeleteServerContent(CurrentPath)
                End Sub
            Item.Buttons = {ToggleButton, DeleteButton}
            PanServerContentFiles.Children.Add(Item)
        Next
    End Sub

    Private Sub ToggleServerContent(FilePath As String)
        Try
            Dim NewPath As String
            If FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) Then
                NewPath = FilePath.Substring(0, FilePath.Length - ".disabled".Length)
            Else
                NewPath = FilePath & ".disabled"
            End If
            If FileUtils.Exists(NewPath) Then
                Hint("目标文件已存在，无法切换启用状态：" & PathUtils.GetLastPart(NewPath), HintType.Red)
                Return
            End If
            FileUtils.Move(FilePath, NewPath)
            RefreshServerContent()
        Catch ex As Exception
            Logger.Error(ex, "切换服务器 Mod/插件启用状态失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub DeleteServerContent(FilePath As String)
        If MyMsgBox($"确定删除该文件吗？{vbCrLf}{PathUtils.GetLastPart(FilePath)}",
                    "删除服务器内容", "删除", "取消", IsWarn:=True) = 2 Then Return
        Try
            FileUtils.Delete(FilePath)
            RefreshServerContent()
        Catch ex As Exception
            Logger.Error(ex, "删除服务器 Mod/插件失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnSaveServerLaunch_Click(sender As Object, e As EventArgs) Handles BtnSaveServerLaunch.Click
        SaveServerSettings(True)
    End Sub

    Private Sub BtnOpenServerLaunchFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenServerLaunchFolder.Click
        If SelectedServerFolder IsNot Nothing Then OpenExplorer(SelectedServerFolder)
    End Sub

    Private Sub BtnServerFileEditor_Click(sender As Object, e As EventArgs) Handles BtnServerFileEditor.Click
        If String.IsNullOrWhiteSpace(SelectedServerFolder) Then
            Hint("请先在启动页左侧选择一个服务器。", HintType.Red)
            Return
        End If
        Try
            Dim EditorWindow As New WindowServerFileEditor(SelectedServerFolder) With {.Owner = FrmMain}
            EditorWindow.ShowDialog()
        Catch ex As Exception
            Logger.Error(ex, "打开服务器文件编辑器失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnSelectServerLaunchJava_Click(sender As Object, e As EventArgs) Handles BtnSelectServerLaunchJava.Click
        Dim JavaPath As String = Dialogs.SelectFile("选择 java.exe", False, filter:="java.exe|java.exe").FirstOrDefault
        If Not String.IsNullOrWhiteSpace(JavaPath) Then ComboServerLaunchJava.Text = JavaPath
    End Sub

    Private Sub ComboServerLaunchJava_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboServerLaunchJava.SelectionChanged
        Dim Item = TryCast(ComboServerLaunchJava.SelectedItem, MyComboBoxItem)
        If Item?.Tag IsNot Nothing Then ComboServerLaunchJava.Text = Item.Tag.ToString
    End Sub

    Private Sub BtnRefreshServerLaunchEula_Click(sender As Object, e As EventArgs) Handles BtnRefreshServerLaunchEula.Click
        RefreshServerEula()
    End Sub

    Private Sub BtnAcceptServerLaunchEula_Click(sender As Object, e As EventArgs) Handles BtnAcceptServerLaunchEula.Click
        If SelectedServerFolder Is Nothing OrElse Not FileUtils.Exists(GetEulaPath(SelectedServerFolder)) Then Return
        If MyMsgBox("请确认你已经阅读并同意 Minecraft EULA。" & vbCrLf &
                    "此操作只会把现有 eula.txt 中的 eula=false 改为 true。",
                    "确认 Minecraft EULA", "我同意", "取消", IsWarn:=True) = 2 Then Return
        If AcceptEula(SelectedServerFolder) Then
            Hint("EULA 已确认。", HintType.Green)
            RefreshServerEula()
        End If
    End Sub

    Private Sub ComboServerContentFolder_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboServerContentFolder.SelectionChanged
        If SelectedServerFolder IsNot Nothing Then RefreshServerContent()
    End Sub

    Private Sub BtnOpenServerContentFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenServerContentFolder.Click
        Dim Folder = GetSelectedServerContentFolder()
        If Folder Is Nothing Then Return
        DirectoryUtils.Create(Folder)
        OpenExplorer(Folder)
    End Sub

    Private Sub BtnAddServerContent_Click(sender As Object, e As EventArgs) Handles BtnAddServerContent.Click
        If SelectedServerFolder Is Nothing Then Return
        Dim Files = Dialogs.SelectFile("选择要加入服务器的 Mod 或插件", True,
            filter:={({"jar", "disabled"}, "Mod 或插件文件")})
        If Not Files.Any() Then Return
        Dim TargetFolder = GetSelectedServerContentFolder()
        DirectoryUtils.Create(TargetFolder)
        For Each Source In Files
            Dim FileName = PathUtils.GetLastPart(Source)
            If Not (FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) OrElse
                    FileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) Then Continue For
            Dim Target = Path.Combine(TargetFolder, FileName)
            If PathUtils.IsSame(Source, Target) Then Continue For
            If FileUtils.Exists(Target) AndAlso
               MyMsgBox($"文件已存在：{FileName}{vbCrLf}是否覆盖？", "确认覆盖", "覆盖", "取消", IsWarn:=True) = 2 Then Continue For
            FileUtils.Copy(Source, Target)
        Next
        RefreshServerContent()
    End Sub

    Private Sub BtnRefreshServerContent_Click(sender As Object, e As EventArgs) Handles BtnRefreshServerContent.Click
        RefreshServerContent()
    End Sub

    Public Sub RefreshServerExternalState()
        If PanServerManager.Visibility = Visibility.Visible Then
            RefreshServerEula()
            RefreshServerContent()
        End If
    End Sub

#End Region

#Region "主页"

    ''' <summary>
    ''' 刷新主页。
    ''' </summary>
    Private Sub Refresh() Handles Me.Loaded
        RunInThread(
        Sub()
            Try
                SyncLock RefreshLock
                    RefreshReal()
                End SyncLock
            Catch ex As Exception
                Logger.Error(ex, "加载 PCL 主页自定义信息失败", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
            End Try
        End Sub)
    End Sub
    Private Sub RefreshReal()
        Dim Content As String = Nothing, Url As String = Nothing
        Select Case Settings.Get(Of Integer)("UiCustomType")
            Case 1
                '加载本地文件
                Logger.Info("主页自定义数据来源：本地文件")
                Content = FileUtils.TryReadAsString(Paths.Base & "PCL\Custom.xaml")
            Case 2
                '联网下载
                Url = Settings.Get(Of String)("UiCustomNet")
            Case 3
                '预设
                Select Case Settings.Get(Of Integer)("UiCustomPreset")
                    Case 0
                        Logger.Info("主页预设：你知道吗")
                        Content = "
                            <local:MyCard Title=""你知道吗？"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{hint}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 1
                        Logger.Info("主页预设：回声洞")
                        Content = "
                            <local:MyCard Title=""回声洞"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{cave}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 2
                        Logger.Info("主页预设：Minecraft 新闻")
                        Url = "https://mcnews.meloong.com"
                    Case 3
                        Logger.Info("主页预设：简单主页")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/MFn233/Custom.xaml"
                    Case 4
                        Logger.Info("主页预设：每日整合包推荐")
                        Url = "https://pclsub.sodamc.com/"
                    Case 5
                        Logger.Info("主页预设：Minecraft 皮肤推荐")
                        Url = "https://forgepixel.com/pcl_sub_file"
                    Case 6
                        Logger.Info("主页预设：OpenBMCLAPI 仪表盘 Lite")
                        Url = "https://pcl-bmcl.milu.ink/"
                    Case 7
                        Logger.Info("主页预设：主页市场")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/p/Homepages/Homepage.Market/Custom.xaml"
                    Case 8
                        Logger.Info("主页预设：更新日志")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Joker2184/UpdateHomepage.xaml"
                    Case 9
                        Logger.Info("主页预设：PCL 新功能说明书")
                        Url = "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml"
                    Case 10
                        Logger.Info("主页预设：OpenMCIM Dashboard")
                        Url = "https://files.mcimirror.top/PCL"
                    Case 11
                        Logger.Info("主页预设：杂志主页")
                        Url = "https://gh-proxy.com/https://github.com/Neclyon/Magazine-Homepage-PCL/raw/main/output/Custom.xaml"
                    Case 12
                        Logger.Info("主页预设：PCL GitHub 仪表盘")
                        Url = "https://ddf.pcl-community.top/Custom.xaml"
                    Case 13
                        Logger.Info("主页预设：PCL 更新摘要")
                        Url = "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml"
                    Case 14
                        Logger.Info("主页预设：今日新闻热点")
                        Url = "https://pcl.wyc-w.top/index.xaml"
                    Case 15
                        Logger.Info("主页预设：Minecraft 芝士站")
                        Url = "https://mkss-services.awaeric.xyz"
                    Case 16
                        Logger.Info("主页预设：整合包推荐引擎")
                        Url = "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml"
                    Case 17
                        Logger.Info("主页预设：Bangumi 番剧主页")
                        Url = "https://bangumi.p.kaphia.top"
                    Case 18
                        Logger.Info("主页预设：Bilibili 热门")
                        Url = "https://bilibili.p.kaphia.top"
                    Case 19
                        Logger.Info("主页预设：Music 云音乐热门")
                        Url = "https://cloudmusic.p.kaphia.top"
                    Case 20
                        Logger.Info("主页预设：模组推荐")
                        Url = "https://g-fish.dpdns.org/download/ModPage.xaml"
                    Case 21
                        Logger.Info("主页预设：每日游戏推荐")
                        Url = "https://pcl.xn--9iq209asnop94b.top/download/GamePage.xaml"
                    Case 22
                        Logger.Info("主页预设：Wallpaper 每日壁纸")
                        Url = "https://wallpaper.p.kaphia.top"
                    Case 23
                        Logger.Info("主页预设：Weather 天气预报")
                        Url = "https://weather-p.kaphia.top/{variable:PCLDailyWeatherPage:choose}.xaml"
                    Case 24
                        Logger.Info("主页预设：新一代狗屁王大杂烩主页")
                        Url = "https://end.g-fish.dpdns.org/Custom.xaml"
                End Select
        End Select
        '联网下载
        If Not String.IsNullOrWhiteSpace(Url) Then
            '替换自定义变量与设置
            Url = ArgumentReplace(Url, AddressOf WebUtility.HtmlEncode)
            If Url = Settings.Get(Of String)("CacheSavedPageUrl") AndAlso FileUtils.Exists(PathTemp & "Cache\Custom.xaml") Then
                '缓存可用
                Logger.Info("主页自定义数据来源：联网缓存文件")
                Content = FileUtils.TryReadAsString(PathTemp & "Cache\Custom.xaml")
                '后台更新缓存
                OnlineLoader.Start((Url, False))
            Else
                '缓存不可用
                Logger.Info("主页自定义数据来源：联网全新下载")
                Hint("正在加载主页……")
                RunInUiWait(Sub() LoadContent(Nothing)) '在加载结束前清空页面
                Settings.Set("CacheSavedPageVersion", "")
                OnlineLoader.Start((Url, True)) '下载完成后将会再次触发更新
                Return
            End If
        End If
        '同步到 UI
        RunInUi(Sub() LoadContent(Content))
    End Sub
    Private RefreshLock As New Object

    '联网获取主页文件
    Private OnlineLoader As New LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer)("下载主页", AddressOf OnlineLoaderSub) With {.ReloadTimeout = 10 * 60 * 1000}
    Private Sub OnlineLoaderSub(Task As LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer))
        Dim Address As String = Task.Input.Address '#3721 中连续触发两次导致内容变化
        Dim ShouldRefresh As Boolean = Task.Input.ShouldRefresh
        Try
            '获取版本校验地址
            Dim VersionAddress As String
            If Address.Contains(".xaml") Then
                VersionAddress = Address.Replace(".xaml", ".xaml.ini")
            Else
                VersionAddress = Address.BeforeFirst("?")
                If Not VersionAddress.EndsWithF("/") Then VersionAddress += "/"
                VersionAddress += "version"
                If Address.Contains("?") Then VersionAddress += "?" & Address.AfterFirst("?")
            End If
            '校验版本
            Dim Version As String = ""
            Try
                Version = NetRequestByClientRetry(VersionAddress)
                If Version.Length > 1000 Then Throw New Exception($"获取的主页版本过长（{Version.Length} 字符）")
                Dim CurrentVersion As String = Settings.Get(Of String)("CacheSavedPageVersion")
                If Version <> "" AndAlso CurrentVersion <> "" AndAlso Version = CurrentVersion Then
                    Logger.Info($"当前缓存的主页已为最新，当前版本：{Version}，检查源：{VersionAddress}")
                    Return
                End If
                Logger.Info($"需要下载联网主页，当前版本：{Version}，检查源：{VersionAddress}")
            Catch exx As Exception
                Logger.Warn(exx, $"联网获取主页版本失败")
                Logger.Info($"无法检查联网主页版本，将直接下载，检查源：{VersionAddress}")
            End Try
            '实际下载
            Dim FileContent As String = NetRequestByClientRetry(Address)
            Logger.Info($"已联网下载主页，内容长度：{FileContent.Length}，来源：{Address}")
            Settings.Set("CacheSavedPageUrl", Address)
            Settings.Set("CacheSavedPageVersion", Version)
            FileUtils.Write(PathTemp & "Cache\Custom.xaml", FileContent)
            '若内容变更则要求刷新
            If LoadedContentHash <> FileContent.GetStableHashCode() AndAlso ShouldRefresh Then Refresh()
        Catch ex As Exception
            Logger.Error(ex, $"下载主页失败（{Address}）", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
        End Try
    End Sub

    ''' <summary>
    ''' 立即强制刷新主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Sub ForceRefresh() Implements IRefreshable.Refresh
        Logger.Info("要求强制刷新主页")
        ClearCache()
        '实际的刷新
        If FrmMain.PageCurrent.Page = FormMain.PageType.Launch Then
            PanBack.ScrollToHome()
            Refresh()
        Else
            FrmMain.PageChange(FormMain.PageType.Launch)
        End If
    End Sub

    ''' <summary>
    ''' 清空主页缓存信息。
    ''' </summary>
    Private Sub ClearCache()
        LoadedContentHash = Nothing
        OnlineLoader.Input = ("", True)
        Settings.Set("CacheSavedPageUrl", "")
        Settings.Set("CacheSavedPageVersion", "")
        Logger.Info("已清空主页缓存")
    End Sub

    ''' <summary>
    ''' 从文本内容中加载主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Private Sub LoadContent(Content As String)
        Try
            SyncLock LoadContentLock
                '如果加载目标内容一致则不加载
                Dim Hash = If(Content, "").GetStableHashCode()
                If Hash = LoadedContentHash Then Return
                LoadedContentHash = Hash
                '实际加载内容
                PanCustom.Children.Clear()
                If String.IsNullOrWhiteSpace(Content) Then
                    Logger.Info($"实例化：清空主页 UI，来源为空")
                    Return
                End If
                Dim LoadStartTime As Date = Date.Now
                '修改时应同时修改 PageOtherHelpDetail.Init
                Content = ArgumentReplace(Content, AddressOf StringUtils.XmlEscape)
                Do While Content.Contains("xmlns")
                    Content = Content.RegexReplace("xmlns[^""']*(""|')[^""']*(""|')", "").Replace("xmlns", "") '禁止声明命名空间
                Loop
                Content = "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:sys=""clr-namespace:System;assembly=mscorlib"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" & Content & "</StackPanel>"
                Logger.Info($"实例化：加载主页 UI 开始，最终内容长度：{Content.Count}")
                PanCustom.Children.Add(GetObjectFromXML(Content))
                '加载计时
                Dim LoadCostTime = (Date.Now - LoadStartTime).Milliseconds
                Logger.Info($"实例化：加载主页 UI 完成，耗时 {LoadCostTime}ms")
                If LoadCostTime > 3000 Then Hint($"主页加载过于缓慢（花费了 {Math.Round(LoadCostTime / 1000, 1)} 秒），请向主页作者反馈此问题，或暂时停止使用该主页")
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, $"加载失败的主页内容：{vbCrLf}{Content}")
            OnLoadContentFailed(ex)
        End Try
    End Sub
    ''' <summary>
    ''' 加载主页失败时调用。
    ''' </summary>
    Private Sub OnLoadContentFailed(ex As Exception)
        If ModeDebug OrElse Settings.Get(Of Integer)("UiCustomType") = 1 Then
            Logger.Warn(ex, "加载主页失败")
            If MyMsgBox(If(TypeOf ex Is UnauthorizedAccessException, ex.Message, $"主页内容编写有误，请根据下列错误信息进行检查：{vbCrLf}{ex.GetDisplay(False)}"),
                        "加载主页失败", "重试", "取消") = 1 Then ForceRefresh()
        Else
            Logger.Error(ex, "加载主页失败", LogBehavior.Toast)
        End If
    End Sub
    ''' <summary>
    ''' 捕获主页在 Measure 和 Arrange 阶段抛出的异常。
    ''' </summary>
    Private Sub DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Implements IDispatcherUnhandledException.DispatcherUnhandledException
        If TypeOf e.Exception IsNot Markup.XamlParseException Then Return
        e.Handled = True
        LoadContent(Nothing)
        OnLoadContentFailed(e.Exception)
    End Sub

    Private LoadedContentHash As ULong? = Nothing
    Private LoadContentLock As New Object

#End Region

End Class
