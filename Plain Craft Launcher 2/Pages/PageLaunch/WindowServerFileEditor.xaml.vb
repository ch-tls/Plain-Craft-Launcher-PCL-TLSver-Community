Imports System.ComponentModel
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Threading

''' <summary>
''' 服务器文件浏览器与编辑器。左侧为服务器目录树，右侧为黑底彩色语法高亮的编辑器。
''' </summary>
Public Class WindowServerFileEditor

    Private Enum EditorSyntax
        Plain
        Yaml
        Json
        Properties
    End Enum

    ''' <summary>超过这个大小就不在启动器内编辑。</summary>
    Private Const NonEditableSize As Long = 2L * 1024 * 1024
    ''' <summary>超过这个大小就不做语法高亮，避免卡顿。</summary>
    Private Const MaxHighlightLength As Integer = 300000

    Private ReadOnly HiddenFolders As String() = {"libraries", "logs", "crash-reports", "cache", ".git"}

    Private ServerFolder As String = Nothing
    Private CurrentFile As String = Nothing
    Private IsDirty As Boolean = False
    Private IsHighlighting As Boolean = False
    Private LastHighlightedText As String = Nothing
    Private Syntax As EditorSyntax = EditorSyntax.Plain
    Private SaveEncoding As Encoding = New UTF8Encoding(False)
    Private HighlightTimer As DispatcherTimer = Nothing

    'VSCode Dark+ 配色
    Private Shared ReadOnly NormalBrush As Brush = MakeBrush(&HD4, &HD4, &HD4)
    Private Shared ReadOnly CommentBrush As Brush = MakeBrush(&H6A, &H99, &H55)
    Private Shared ReadOnly KeyBrush As Brush = MakeBrush(&H9C, &HDC, &HFE)
    Private Shared ReadOnly StringBrush As Brush = MakeBrush(&HCE, &H91, &H78)
    Private Shared ReadOnly NumberBrush As Brush = MakeBrush(&HB5, &HCE, &HA8)
    Private Shared ReadOnly KeywordBrush As Brush = MakeBrush(&H56, &H9C, &HD6)

    Private Shared ReadOnly YamlPattern As New Regex(
        "(?<comment>#.*$)" &
        "|(?<string>""(?:[^""\\]|\\.)*""|'[^']*')" &
        "|(?<key>^[ \t]*(?:-[ \t]+)*[^#:\r\n][^#:\r\n]*?(?=\s*:))" &
        "|(?<keyword>\b(?:true|false|yes|no|on|off|null|~)\b)" &
        "|(?<number>-?\d+(?:\.\d+)?)", RegexOptions.Compiled)

    Private Shared ReadOnly JsonPattern As New Regex(
        "(?<comment>//.*$)" &
        "|(?<string>""(?:[^""\\]|\\.)*"")" &
        "|(?<keyword>\b(?:true|false|null)\b)" &
        "|(?<number>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)", RegexOptions.Compiled)

    Private Shared ReadOnly PropertiesPattern As New Regex(
        "(?<comment>[#!].*$)" &
        "|(?<string>""(?:[^""\\]|\\.)*""|'[^']*')" &
        "|(?<key>^[ \t]*[^#!=: \t][^=:\r\n]*?(?=\s*[=:]))" &
        "|(?<keyword>\b(?:true|false)\b)" &
        "|(?<number>-?\d+(?:\.\d+)?)", RegexOptions.Compiled)

    Private Shared Function MakeBrush(R As Byte, G As Byte, B As Byte) As Brush
        Dim Result As New SolidColorBrush(Color.FromRgb(R, G, B))
        Result.Freeze()
        Return Result
    End Function

    Public Sub New()
        InitializeComponent()
        HighlightTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(350)}
        AddHandler HighlightTimer.Tick, AddressOf HighlightTimerTick
        AddHandler Me.Loaded, AddressOf WindowServerFileEditor_Loaded
    End Sub

    Public Sub New(Folder As String, Optional InitialFile As String = Nothing)
        Me.New()
        ServerFolder = Folder
        _InitialFile = InitialFile
    End Sub

    Private _InitialFile As String = Nothing

    Private Sub WindowServerFileEditor_Loaded(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(ServerFolder) AndAlso Not String.IsNullOrWhiteSpace(FrmLaunchRight?.SelectedServerFolder) Then
            ServerFolder = FrmLaunchRight.SelectedServerFolder
        End If
        BuildTree()
        If Not String.IsNullOrWhiteSpace(_InitialFile) AndAlso FileUtils.Exists(_InitialFile) Then LoadFile(_InitialFile)
    End Sub

#Region "文件树"

    Private Sub BuildTree()
        TreeFiles.Items.Clear()
        If String.IsNullOrWhiteSpace(ServerFolder) OrElse Not Directory.Exists(ServerFolder) Then
            LabStatus.Text = "服务器目录不存在，无法浏览文件。"
            Return
        End If
        Dim Root As New TreeViewItem With {
            .Header = PathUtils.GetLastPart(ServerFolder),
            .Tag = ServerFolder,
            .FontWeight = FontWeights.SemiBold
        }
        Root.Items.Add(New TreeViewItem With {.Header = "…"})
        AddHandler Root.Expanded, AddressOf TreeNodeExpanded
        TreeFiles.Items.Add(Root)
        Root.IsExpanded = True
        PopulateNode(Root)
        LabStatus.Text = "已加载服务器目录：" & ServerFolder
    End Sub

    Private Sub TreeNodeExpanded(sender As Object, e As RoutedEventArgs)
        PopulateNode(TryCast(sender, TreeViewItem))
    End Sub

    Private Sub PopulateNode(Node As TreeViewItem)
        If Node Is Nothing Then Return
        Dim Folder As String = If(Node.Tag?.ToString, "")
        If String.IsNullOrWhiteSpace(Folder) OrElse Not Directory.Exists(Folder) Then Return
        '已经加载过真实子项就不再重复枚举
        If Node.Items.Count > 0 Then
            Dim FirstItem = TryCast(Node.Items(0), TreeViewItem)
            If FirstItem IsNot Nothing AndAlso FirstItem.Tag IsNot Nothing Then Return
        End If
        Node.Items.Clear()
        Try
            Dim Dirs = Directory.EnumerateDirectories(Folder).
                Where(Function(d) Not HiddenFolders.Contains(PathUtils.GetLastPart(d), StringComparer.OrdinalIgnoreCase)).
                OrderBy(Function(d) PathUtils.GetLastPart(d), StringComparer.OrdinalIgnoreCase)
            For Each DirPath As String In Dirs
                Dim DirNode As New TreeViewItem With {
                    .Header = PathUtils.GetLastPart(DirPath),
                    .Tag = DirPath,
                    .FontWeight = FontWeights.SemiBold
                }
                DirNode.Items.Add(New TreeViewItem With {.Header = "…"})
                AddHandler DirNode.Expanded, AddressOf TreeNodeExpanded
                Node.Items.Add(DirNode)
            Next
            Dim Files = Directory.EnumerateFiles(Folder).
                OrderBy(Function(f) PathUtils.GetLastPart(f), StringComparer.OrdinalIgnoreCase)
            For Each FilePath As String In Files
                Node.Items.Add(New TreeViewItem With {.Header = PathUtils.GetLastPart(FilePath), .Tag = FilePath})
            Next
        Catch ex As Exception
            Logger.Warn(ex, "枚举服务器目录失败：" & Folder)
        End Try
    End Sub

    Private Sub TreeFiles_SelectedItemChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Object))
        Dim Node = TryCast(e.NewValue, TreeViewItem)
        If Node Is Nothing Then Return
        Dim FilePath As String = If(Node.Tag?.ToString, "")
        If String.IsNullOrWhiteSpace(FilePath) OrElse Not FileUtils.Exists(FilePath) Then Return
        If String.Equals(FilePath, CurrentFile, StringComparison.OrdinalIgnoreCase) Then Return
        If Not TrySaveBeforeLeave() Then Return
        LoadFile(FilePath)
    End Sub

#End Region

#Region "读取与保存"

    Private Sub LoadFile(FilePath As String)
        Try
            If Not FileUtils.Exists(FilePath) Then
                LabStatus.Text = "文件不存在：" & FilePath
                Return
            End If

            Dim Info = FileUtils.GetInfo(FilePath)
            CurrentFile = FilePath
            Syntax = GetSyntax(FilePath)

            If Info.Length > NonEditableSize Then
                Editor.IsReadOnly = True
                IsDirty = False
                LastHighlightedText = Nothing
                ShowReadonlyText($"（该文件大小 {FormatSize(Info.Length)}，超过 2 MB，不支持在启动器内编辑。请使用外部编辑器打开。）")
                UpdateTitleAndStatus()
                Return
            End If

            Dim Bytes = FileUtils.ReadAsBytes(FilePath)
            If IsBinaryContent(Bytes) Then
                Editor.IsReadOnly = True
                IsDirty = False
                LastHighlightedText = Nothing
                ShowReadonlyText("（该文件看起来是二进制文件，不支持在启动器内编辑。）")
                UpdateTitleAndStatus()
                Return
            End If

            Dim Text As String
            If Bytes.Length >= 3 AndAlso Bytes(0) = CByte(&HEF) AndAlso Bytes(1) = CByte(&HBB) AndAlso Bytes(2) = CByte(&HBF) Then
                SaveEncoding = New UTF8Encoding(True)
                Text = (New UTF8Encoding(False)).GetString(Bytes, 3, Bytes.Length - 3)
            Else
                Try
                    Text = (New UTF8Encoding(False, True)).GetString(Bytes)
                    SaveEncoding = New UTF8Encoding(False)
                Catch
                    '不是合法的 UTF-8（例如 GBK 编码的配置），按系统默认编码读写
                    Text = Encoding.Default.GetString(Bytes)
                    SaveEncoding = Encoding.Default
                End Try
            End If

            Editor.IsReadOnly = False
            IsHighlighting = True
            Try
                RebuildDocument(Text, True)
            Finally
                IsHighlighting = False
            End Try
            LastHighlightedText = Text
            IsDirty = False
            UpdateTitleAndStatus()
            LabStatus.Text = "已打开：" & FilePath
        Catch ex As Exception
            Logger.Error(ex, "打开服务器文件失败")
            MessageBox.Show(Me, ex.Message, "打开文件失败", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub ShowReadonlyText(Text As String)
        IsHighlighting = True
        Try
            RebuildDocument(Text, False)
        Finally
            IsHighlighting = False
        End Try
    End Sub

    Private Sub SaveCurrentFile()
        If CurrentFile Is Nothing OrElse Editor.IsReadOnly Then Return
        Try
            Dim Text As String = GetEditorText()
            FileUtils.WriteAtomic(CurrentFile, Text, SaveEncoding)
            LastHighlightedText = Text
            IsDirty = False
            UpdateTitleAndStatus()
            LabStatus.Text = "已保存：" & CurrentFile
        Catch ex As Exception
            Logger.Error(ex, "保存服务器文件失败")
            MessageBox.Show(Me, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    ''' <summary>离开当前文件前处理未保存的修改，返回 False 表示取消。</summary>
    Private Function TrySaveBeforeLeave() As Boolean
        If Not IsDirty OrElse CurrentFile Is Nothing Then Return True
        '这里用系统弹窗而不是 MyMsgBox：MyMsgBox 的遮罩画在主窗口上，
        '而本窗口是模态的，主窗口被禁用后遮罩点不到会卡住。
        Dim Caption As String = "“" & PathUtils.GetLastPart(CurrentFile) & "”有未保存的修改，要先保存吗？"
        Select Case MessageBox.Show(Me, Caption, "未保存的修改", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)
            Case MessageBoxResult.Yes
                SaveCurrentFile()
                Return Not IsDirty
            Case MessageBoxResult.No
                Return True
            Case Else
                Return False
        End Select
    End Function

#End Region

#Region "编辑器与高亮"

    Private Sub RebuildDocument(Text As String, Highlight As Boolean)
        Dim Doc As New FlowDocument With {
            .PagePadding = New Thickness(0),
            .FontFamily = Editor.FontFamily,
            .FontSize = Editor.FontSize,
            .Foreground = NormalBrush
        }
        Dim Lines = Text.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
        For Each Line As String In Lines
            Dim Para As New Paragraph With {.Margin = New Thickness(0)}
            AddLineRuns(Para, Line, Highlight)
            Doc.Blocks.Add(Para)
        Next
        Editor.Document = Doc
    End Sub

    Private Sub AddLineRuns(Para As Paragraph, Line As String, Highlight As Boolean)
        Dim Pattern = GetSyntaxPattern()
        If Not Highlight OrElse Line.Length = 0 OrElse Pattern Is Nothing Then
            Para.Inlines.Add(New Run(Line) With {.Foreground = NormalBrush})
            Return
        End If
        Dim Last As Integer = 0
        For Each M As Match In Pattern.Matches(Line)
            If M.Length = 0 Then Continue For
            If M.Index > Last Then
                Para.Inlines.Add(New Run(Line.Substring(Last, M.Index - Last)) With {.Foreground = NormalBrush})
            End If
            Dim Brush As Brush = NormalBrush
            If M.Groups("comment").Success Then
                Brush = CommentBrush
            ElseIf M.Groups("string").Success Then
                Brush = If(Syntax = EditorSyntax.Json AndAlso IsJsonKey(Line, M), KeyBrush, StringBrush)
            ElseIf M.Groups("key").Success Then
                Brush = KeyBrush
            ElseIf M.Groups("keyword").Success Then
                Brush = KeywordBrush
            ElseIf M.Groups("number").Success Then
                Brush = NumberBrush
            End If
            Para.Inlines.Add(New Run(M.Value) With {.Foreground = Brush})
            Last = M.Index + M.Length
        Next
        If Last < Line.Length Then
            Para.Inlines.Add(New Run(Line.Substring(Last)) With {.Foreground = NormalBrush})
        End If
    End Sub

    Private Function GetSyntaxPattern() As Regex
        Select Case Syntax
            Case EditorSyntax.Yaml
                Return YamlPattern
            Case EditorSyntax.Json
                Return JsonPattern
            Case EditorSyntax.Properties
                Return PropertiesPattern
            Case Else
                Return Nothing
        End Select
    End Function

    Private Shared Function GetSyntax(FilePath As String) As EditorSyntax
        Select Case PathUtils.GetExtension(FilePath).ToLowerInvariant()
            Case "yml", "yaml"
                Return EditorSyntax.Yaml
            Case "json", "json5", "jsonc"
                Return EditorSyntax.Json
            Case "properties", "conf", "cfg", "ini", "toml"
                Return EditorSyntax.Properties
            Case Else
                Return EditorSyntax.Plain
        End Select
    End Function

    Private Shared Function IsJsonKey(Line As String, M As Match) As Boolean
        Dim Index As Integer = M.Index + M.Length
        While Index < Line.Length AndAlso Char.IsWhiteSpace(Line(Index))
            Index += 1
        End While
        Return Index < Line.Length AndAlso Line(Index) = ":"c
    End Function

    Private Sub ApplyHighlight()
        If IsHighlighting OrElse CurrentFile Is Nothing OrElse Editor.IsReadOnly Then Return
        Dim Text As String = GetEditorText()
        If Text = LastHighlightedText Then Return
        If Text.Length > MaxHighlightLength Then Return
        Dim CaretOffset As Integer = GetCaretOffset()
        Dim VerticalOffset As Double = GetVerticalScrollOffset()
        IsHighlighting = True
        Try
            RebuildDocument(Text, True)
        Catch ex As Exception
            Logger.Warn(ex, "刷新语法高亮失败")
            IsHighlighting = False
            Return
        Finally
            IsHighlighting = False
        End Try
        LastHighlightedText = Text
        RestoreCaret(CaretOffset)
        SetVerticalScrollOffset(VerticalOffset)
        UpdateTitleAndStatus()
    End Sub

    Private Function GetEditorText() As String
        Dim Text As String = New TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text
        If Text.EndsWith(vbCrLf) Then Text = Text.Substring(0, Text.Length - 2)
        Return Text
    End Function

    Private Function GetCaretOffset() As Integer
        Try
            Return Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition)
        Catch
            Return 0
        End Try
    End Function

    Private Sub RestoreCaret(Offset As Integer)
        If Offset <= 0 Then Return
        Try
            Dim Pos = Editor.Document.ContentStart.GetPositionAtOffset(Offset, LogicalDirection.Forward)
            If Pos IsNot Nothing Then Editor.CaretPosition = Pos
        Catch
        End Try
    End Sub

    Private Function FindScrollViewer(Element As DependencyObject) As ScrollViewer
        If Element Is Nothing Then Return Nothing
        Dim Viewer = TryCast(Element, ScrollViewer)
        If Viewer IsNot Nothing Then Return Viewer
        Dim Count As Integer = VisualTreeHelper.GetChildrenCount(Element)
        For i = 0 To Count - 1
            Dim Result = FindScrollViewer(VisualTreeHelper.GetChild(Element, i))
            If Result IsNot Nothing Then Return Result
        Next
        Return Nothing
    End Function

    Private Function GetVerticalScrollOffset() As Double
        Try
            Dim Viewer = FindScrollViewer(Editor)
            Return If(Viewer Is Nothing, 0, Viewer.VerticalOffset)
        Catch
            Return 0
        End Try
    End Function

    Private Sub SetVerticalScrollOffset(Offset As Double)
        Try
            If Offset <= 0 Then Return
            Dim Viewer = FindScrollViewer(Editor)
            If Viewer IsNot Nothing Then Viewer.ScrollToVerticalOffset(Offset)
        Catch
        End Try
    End Sub

    Private Sub HighlightTimerTick(sender As Object, e As EventArgs)
        HighlightTimer.Stop()
        ApplyHighlight()
    End Sub

#End Region

#Region "界面事件"

    Private Sub Editor_TextChanged(sender As Object, e As TextChangedEventArgs) Handles Editor.TextChanged
        If IsHighlighting OrElse CurrentFile Is Nothing OrElse Editor.IsReadOnly Then Return
        If Not IsDirty Then
            IsDirty = True
            UpdateTitleAndStatus()
        End If
        If HighlightTimer IsNot Nothing Then
            HighlightTimer.Stop()
            HighlightTimer.Start()
        End If
    End Sub

    Private Sub Editor_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.S AndAlso Keyboard.Modifiers = ModifierKeys.Control Then
            SaveCurrentFile()
            e.Handled = True
            Return
        End If
        '用两个空格代替 Tab，避免 YAML 缩进被写入制表符
        If e.Key = Key.Tab AndAlso (Keyboard.Modifiers And (ModifierKeys.Control Or ModifierKeys.Alt)) = 0 Then
            Editor.Selection.Text = "  "
            e.Handled = True
        End If
    End Sub

    Private Sub BtnSave_Click(sender As Object, e As EventArgs) Handles BtnSave.Click
        SaveCurrentFile()
    End Sub

    Private Sub BtnReload_Click(sender As Object, e As EventArgs) Handles BtnReload.Click
        If CurrentFile Is Nothing Then Return
        If Not TrySaveBeforeLeave() Then Return
        LoadFile(CurrentFile)
    End Sub

    Private Sub BtnRefreshTree_Click(sender As Object, e As EventArgs) Handles BtnRefreshTree.Click
        If Not TrySaveBeforeLeave() Then Return
        BuildTree()
    End Sub

    Private Sub BtnOpenFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenFolder.Click
        Dim Folder = If(String.IsNullOrWhiteSpace(CurrentFile), ServerFolder, PathUtils.RemoveLastPart(CurrentFile))
        If Not String.IsNullOrWhiteSpace(Folder) Then OpenExplorer(Folder)
    End Sub

    Private Sub WindowServerFileEditor_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If HighlightTimer IsNot Nothing Then HighlightTimer.Stop()
        If Not TrySaveBeforeLeave() Then e.Cancel = True
    End Sub

    Private Sub UpdateTitleAndStatus()
        If CurrentFile Is Nothing Then
            Title = "服务器文件浏览与编辑"
            LabTitle.Text = "未选择文件"
            LabStatusRight.Text = ""
            BtnSave.IsEnabled = False
            Return
        End If
        Dim Name As String = PathUtils.GetLastPart(CurrentFile)
        Title = "服务器文件浏览与编辑 - " & Name
        LabTitle.Text = CurrentFile & If(IsDirty, "  ●", "")
        BtnSave.IsEnabled = Not Editor.IsReadOnly
        Dim Text As String = GetEditorText()
        Dim LineCount As Integer = Text.Replace(vbCrLf, vbLf).Split(CChar(vbLf)).Length
        LabStatusRight.Text = $"行 {LineCount} · 字符 {Text.Length} · {SaveEncoding.WebName.ToUpperInvariant()}" &
            If(Editor.IsReadOnly, " · 只读", "") & If(IsDirty, " · 已修改", "")
    End Sub

    Private Shared Function IsBinaryContent(Bytes As Byte()) As Boolean
        If Bytes Is Nothing OrElse Bytes.Length = 0 Then Return False
        Dim Limit As Integer = Math.Min(Bytes.Length, 8000)
        For i = 0 To Limit - 1
            If Bytes(i) = 0 Then Return True
        Next
        Return False
    End Function

    Private Shared Function FormatSize(Size As Long) As String
        If Size >= 1024L * 1024 Then Return (Size / 1024.0 / 1024.0).ToString("0.0") & " MB"
        If Size >= 1024 Then Return (Size / 1024.0).ToString("0.0") & " KB"
        Return Size & " B"
    End Function

#End Region

End Class
