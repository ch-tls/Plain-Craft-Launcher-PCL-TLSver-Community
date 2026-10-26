Public Module ModServer

    Public Enum ServerCoreType
        Vanilla
        Paper
        Purpur
        Fabric
        Forge
        NeoForge
        MSLMC
    End Enum

    Public Class ServerLaunchInfo
        Public LaunchMode As String = "Jar"
        Public CoreJar As String = ""
        Public LaunchArgsFile As String = ""
    End Class

    Public ReadOnly Property ServerRoot As String
        Get
            Return Paths.Base & "PCL\Servers\"
        End Get
    End Property

    Public Sub EnsureServerRoot()
        DirectoryUtils.Create(ServerRoot)
    End Sub

    Public Function GetServerFolders() As List(Of String)
        EnsureServerRoot()
        Return DirectoryUtils.EnumerateDirectories(ServerRoot, False).
            OrderBy(Function(folder) PathUtils.GetLastPart(folder), StringComparer.OrdinalIgnoreCase).ToList
    End Function

    Public Function GetServerFolder(FolderName As String) As String
        EnsureServerRoot()
        Dim Validator As New ValidateFolderName(ServerRoot)
        Dim ErrorMessage As String = Validator.Validate(FolderName)
        If Not String.IsNullOrEmpty(ErrorMessage) Then Throw New ArgumentException(ErrorMessage)
        Return Path.Combine(ServerRoot, FolderName)
    End Function

    Public Function LoadServerConfig(Folder As String) As JObject
        Dim ConfigPath As String = Path.Combine(Folder, "PCL\Server.json")
        Dim Config As JObject
        Try
            Config = If(FileUtils.Exists(ConfigPath), CType(FileUtils.ReadAsJson(ConfigPath), JObject), New JObject)
        Catch
            Config = New JObject
        End Try
        If Not Config.ContainsKey("Name") Then Config("Name") = PathUtils.GetLastPart(Folder)
        Dim LaunchInfo As ServerLaunchInfo = FindServerLaunchInfo(Folder)
        If Not Config.ContainsKey("CoreType") Then Config("CoreType") = InferServerCoreType(Folder, Config).ToString
        If Not Config.ContainsKey("MinecraftVersion") Then Config("MinecraftVersion") = InferMinecraftVersion(Folder, Config)
        If Not Config.ContainsKey("LaunchMode") Then Config("LaunchMode") = LaunchInfo.LaunchMode
        If Not Config.ContainsKey("CoreJar") Then Config("CoreJar") = LaunchInfo.CoreJar
        If Not Config.ContainsKey("LaunchArgsFile") Then Config("LaunchArgsFile") = LaunchInfo.LaunchArgsFile
        If Not Config.ContainsKey("JavaPath") Then Config("JavaPath") = If(FindJavaFromEnvironment(), "java.exe")
        If Not Config.ContainsKey("XmsMB") Then Config("XmsMB") = 1024
        If Not Config.ContainsKey("XmxMB") Then Config("XmxMB") = 4096
        If Not Config.ContainsKey("Port") Then Config("Port") = 25565
        If Not Config.ContainsKey("NoGui") Then Config("NoGui") = True
        If Not Config.ContainsKey("JvmArgs") Then Config("JvmArgs") = "-XX:+UseG1GC -Dfile.encoding=UTF-8"
        If Not Config.ContainsKey("ServerArgs") Then Config("ServerArgs") = ""
        Return Config
    End Function

    Public Sub SaveServerConfig(Folder As String, Config As JObject)
        Dim ConfigPath As String = Path.Combine(Folder, "PCL\Server.json")
        DirectoryUtils.Create(PathUtils.RemoveLastPart(ConfigPath))
        FileUtils.Write(ConfigPath, Config.ToString(Newtonsoft.Json.Formatting.Indented))
    End Sub

    Public Function DetectServerJars(Folder As String) As List(Of String)
        Return DirectoryUtils.EnumerateFiles(Folder, False, "*.jar").
            Select(Function(path) path.Replace(Folder & "\", "")).
            OrderByDescending(Function(name) IsLikelyServerJar(name)).
            ThenBy(Function(name) name, StringComparer.OrdinalIgnoreCase).
            ToList
    End Function

    Public Function IsLikelyServerJar(Name As String) As Boolean
        Dim LowerName As String = Name.ToLowerInvariant
        If LowerName.Contains("installer") OrElse LowerName.Contains("client") Then Return False
        Return {"server", "paper", "purpur", "fabric", "forge", "neoforge", "arclight", "catserver", "mohist", "youer", "silkard"}.Any(Function(keyword) LowerName.Contains(keyword))
    End Function

    Public Function InferServerCoreType(Folder As String, Config As JObject) As ServerCoreType
        Dim StoredName As String = Config?.Item("CoreType")?.ToString
        '仅迁移旧配置，不再访问或展示旧镜像源。
        If String.Equals(StoredName, "FastMirror", StringComparison.OrdinalIgnoreCase) OrElse
           String.Equals(StoredName, "MohistMC", StringComparison.OrdinalIgnoreCase) Then Return ServerCoreType.MSLMC
        Dim StoredType As ServerCoreType
        If Not String.IsNullOrWhiteSpace(StoredName) AndAlso
           [Enum].TryParse(StoredName, True, StoredType) Then Return StoredType
        Dim CoreName As String = If(Config?.Item("CoreJar")?.ToString, DetectServerJars(Folder).FirstOrDefault)
        If Not String.IsNullOrWhiteSpace(CoreName) Then CoreName = CoreName.ToLowerInvariant
        If CoreName.Contains("neoforge") Then Return ServerCoreType.NeoForge
        If CoreName.Contains("forge") Then Return ServerCoreType.Forge
        If CoreName.Contains("fabric") Then Return ServerCoreType.Fabric
        If CoreName.Contains("purpur") Then Return ServerCoreType.Purpur
        If CoreName.Contains("paper") Then Return ServerCoreType.Paper
        Dim LaunchArgs As String = If(Config?.Item("LaunchArgsFile")?.ToString, FindServerLaunchInfo(Folder).LaunchArgsFile)
        If Not String.IsNullOrWhiteSpace(LaunchArgs) Then
            If LaunchArgs.ContainsIgnoreCase("neoforged/neoforge") Then Return ServerCoreType.NeoForge
            If LaunchArgs.ContainsIgnoreCase("minecraftforge/forge") Then Return ServerCoreType.Forge
        End If
        Return ServerCoreType.Vanilla
    End Function

    Public Function SupportsServerPlugins(Folder As String, Config As JObject) As Boolean
        If Config?.Item("SupportsPlugins") IsNot Nothing Then Return Config("SupportsPlugins").ToObject(Of Boolean)
        Dim CoreType As ServerCoreType = InferServerCoreType(Folder, Config)
        If CoreType = ServerCoreType.Paper OrElse CoreType = ServerCoreType.Purpur Then Return True
        Return CoreType = ServerCoreType.MSLMC AndAlso
            SupportsPluginsForCore(GetConfiguredCoreName(Config))
    End Function

    Public Function GetConfiguredCoreName(Config As JObject) As String
        Dim MslmcCore As String = Config?.Item("MslmcCore")?.ToString
        If Not String.IsNullOrWhiteSpace(MslmcCore) Then Return MslmcCore
        Dim MirrorCore As String = Config?.Item("FastMirrorCore")?.ToString
        If Not String.IsNullOrWhiteSpace(MirrorCore) Then Return MirrorCore
        Dim MohistCore As String = Config?.Item("MohistCore")?.ToString
        If Not String.IsNullOrWhiteSpace(MohistCore) Then Return MohistCore
        Return If(Config?.Item("CoreType")?.ToString, "Vanilla")
    End Function

    Public Function SupportsServerMods(Folder As String, Config As JObject) As Boolean
        If Config?.Item("SupportsMods") IsNot Nothing Then Return Config("SupportsMods").ToObject(Of Boolean)
        Dim CoreType As ServerCoreType = InferServerCoreType(Folder, Config)
        Return CoreType = ServerCoreType.Fabric OrElse CoreType = ServerCoreType.Forge OrElse
            CoreType = ServerCoreType.NeoForge OrElse
            (CoreType = ServerCoreType.MSLMC AndAlso SupportsModsForCore(GetConfiguredCoreName(Config)))
    End Function

    Public Function InferMinecraftVersion(Folder As String, Config As JObject) As String
        Dim StoredVersion As String = Config?.Item("MinecraftVersion")?.ToString
        If Not String.IsNullOrWhiteSpace(StoredVersion) Then Return StoredVersion.Trim
        Dim Names As New List(Of String) From {Config?.Item("CoreJar")?.ToString}
        Names.AddRange(DirectoryUtils.EnumerateFiles(Folder, False, "*.jar").Select(AddressOf PathUtils.GetLastPart))
        For Each Name In Names.Where(Function(n) Not String.IsNullOrWhiteSpace(n))
            Dim Match = System.Text.RegularExpressions.Regex.Match(
                Name, "(?<!\d)(1\.\d+(?:\.\d+)?|2[0-9]\.\d+(?:\.\d+)?)(?![\d.])")
            If Match.Success Then Return Match.Value
        Next
        Return ""
    End Function

    Public Function FindServerLaunchInfo(Folder As String, Optional CoreType As ServerCoreType = ServerCoreType.Vanilla) As ServerLaunchInfo
        Dim Result As New ServerLaunchInfo
        Dim Jars = DetectServerJars(Folder)
        If CoreType = ServerCoreType.Fabric Then
            Dim FabricJar = Jars.FirstOrDefault(Function(name) name.ToLowerInvariant.Contains("fabric"))
            If FabricJar IsNot Nothing Then Result.CoreJar = FabricJar
            Return Result
        End If
        If CoreType = ServerCoreType.Forge OrElse CoreType = ServerCoreType.NeoForge OrElse CoreType = ServerCoreType.Vanilla Then
            Dim LibraryFolder As String = Path.Combine(Folder, "libraries")
            If DirectoryUtils.Exists(LibraryFolder) Then
                Dim ArgsFiles = DirectoryUtils.EnumerateFiles(LibraryFolder, True, "win_args.txt").
                    Where(Function(path)
                              Dim LowerPath As String = path.ToLowerInvariant
                              Select Case CoreType
                                  Case ServerCoreType.Forge
                                      Return LowerPath.Contains("minecraftforge")
                                  Case ServerCoreType.NeoForge
                                      Return LowerPath.Contains("neoforged")
                                  Case ServerCoreType.Vanilla
                                      Return LowerPath.Contains("minecraftforge") OrElse LowerPath.Contains("neoforged")
                                  Case Else
                                      Return False
                              End Select
                          End Function).
                    OrderByDescending(Function(path) path.ToLowerInvariant.Contains("neoforge") = (CoreType = ServerCoreType.NeoForge)).
                    ToList
                If ArgsFiles.Any Then
                    Result.LaunchMode = "Args"
                    Result.LaunchArgsFile = ArgsFiles.First.Replace(Folder & "\", "").Replace("\", "/")
                    Return Result
                End If
            End If
        End If
        Dim Jar = Jars.FirstOrDefault
        If Not String.IsNullOrWhiteSpace(Jar) Then Result.CoreJar = Jar
        Return Result
    End Function

    Public Function GetServerContentFolder(CoreType As ServerCoreType) As String
        If CoreType = ServerCoreType.Paper OrElse CoreType = ServerCoreType.Purpur Then Return "plugins"
        Return "mods"
    End Function

    Public Function GetServerContentFolder(Folder As String, Config As JObject) As String
        If SupportsServerPlugins(Folder, Config) AndAlso Not SupportsServerMods(Folder, Config) Then Return "plugins"
        Return GetServerContentFolder(InferServerCoreType(Folder, Config))
    End Function

    Public Sub DeleteCreatedServerFolder(Folder As String)
        If String.IsNullOrWhiteSpace(Folder) OrElse Not DirectoryUtils.Exists(Folder) Then Return
        Dim Root As String = PathUtils.RemoveSlashSuffix(ServerRoot)
        Dim Target As String = PathUtils.RemoveSlashSuffix(Folder)
        If PathUtils.IsSame(Root, Target) OrElse Not PathUtils.IsParentOf(Root, Target) Then
            Throw New InvalidOperationException("拒绝删除服务器根目录之外的路径：" & Folder)
        End If
        DirectoryUtils.Delete(Target)
    End Sub

    Public Function FindJavaFromEnvironment() As String
        Dim JavaHome As String = Environment.GetEnvironmentVariable("JAVA_HOME")
        If Not String.IsNullOrWhiteSpace(JavaHome) Then
            Dim Candidate As String = Path.Combine(JavaHome, "bin\java.exe")
            If FileUtils.Exists(Candidate) Then Return Candidate
        End If
        For Each Folder In Environment.GetEnvironmentVariable("PATH").Split(";"c)
            Dim Candidate As String = Path.Combine(Folder.Trim(""""c), "java.exe")
            If FileUtils.Exists(Candidate) Then Return Candidate
        Next
        Return Nothing
    End Function

    Public Function FindRecommendedJava(MinecraftVersion As String) As String
        Dim RecommendedMajor As Integer = GetRecommendedJavaMajor(MinecraftVersion)
        Dim MatchedJava = Configs.JavaList.Get().
            Where(Function(java) java.Version IsNot Nothing AndAlso java.Version.Major = RecommendedMajor).
            FirstOrDefault
        Return If(MatchedJava?.JavaExePath, FindJavaFromEnvironment())
    End Function

    ''' <summary>
    ''' 获取指定 Minecraft 版本推荐使用的 Java 主版本号。
    ''' </summary>
    Public Function GetRecommendedJavaMajor(MinecraftVersion As String) As Integer
        If MinecraftVersion.StartsWith("26.") Then Return 25
        If Not MinecraftVersion.StartsWith("1.") Then Return 21
        Dim Parts = MinecraftVersion.Split(".")
        Dim Minor As Integer = If(Parts.Length > 1, Val(Parts(1)), 0)
        Dim Patch As Integer = If(Parts.Length > 2, Val(Parts(2)), 0)
        If Minor >= 21 OrElse (Minor = 20 AndAlso Patch >= 5) Then Return 21
        If Minor >= 18 Then Return 17
        If Minor = 17 Then Return 16
        Return 8
    End Function

    Public Function QuoteCommandLineArgument(Value As String) As String
        If Not Value.Any(Function(ch As Char) Char.IsWhiteSpace(ch) OrElse ch = ControlChars.Quote) Then Return Value
        Return """" & Value.Replace("""", "\""") & """"
    End Function

    Public Sub WriteServerPort(Folder As String, Port As Integer)
        Dim PropertiesPath As String = Path.Combine(Folder, "server.properties")
        Dim Lines As New List(Of String)
        If FileUtils.Exists(PropertiesPath) Then Lines = FileUtils.ReadAsString(PropertiesPath).Replace(vbCrLf, vbLf).Split(vbLf).ToList
        Dim Found As Boolean = False
        For i = 0 To Lines.Count - 1
            If Lines(i).TrimStart.StartsWith("server-port=", StringComparison.OrdinalIgnoreCase) Then
                Lines(i) = "server-port=" & Port
                Found = True
            End If
        Next
        If Not Found Then Lines.Add("server-port=" & Port)
        FileUtils.Write(PropertiesPath, Lines.Join(vbCrLf))
    End Sub

    Public Function GetEulaPath(Folder As String) As String
        Return Path.Combine(Folder, "eula.txt")
    End Function

    Public Function IsEulaAccepted(Folder As String) As Boolean
        Dim EulaPath As String = GetEulaPath(Folder)
        If Not FileUtils.Exists(EulaPath) Then Return False
        Return FileUtils.ReadAsString(EulaPath).Replace(" ", "").Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).
            Any(Function(line) line.StartsWith("eula=true", StringComparison.OrdinalIgnoreCase))
    End Function

    Public Function AcceptEula(Folder As String) As Boolean
        Dim EulaPath As String = GetEulaPath(Folder)
        If Not FileUtils.Exists(EulaPath) Then Return False
        Dim Lines = FileUtils.ReadAsString(EulaPath).Replace(vbCrLf, vbLf).Split(vbLf).ToList
        Dim Found As Boolean = False
        For i = 0 To Lines.Count - 1
            If Lines(i).TrimStart.StartsWith("eula=", StringComparison.OrdinalIgnoreCase) Then
                Lines(i) = "eula=true"
                Found = True
            End If
        Next
        If Not Found Then Lines.Add("eula=true")
        FileUtils.Write(EulaPath, Lines.Join(vbCrLf))
        Return True
    End Function

    Public Function BuildServerLaunchScript(Folder As String, Config As JObject) As String
        If String.IsNullOrWhiteSpace(Folder) OrElse Not DirectoryUtils.Exists(Folder) Then
            Throw New Exception("服务器目录不存在：" & Folder)
        End If
        Dim JavaPath As String = ResolveServerJavaPath(Folder, Config("JavaPath")?.ToString)
        Dim LaunchMode As String = If(Config("LaunchMode")?.ToString, "Jar")
        Dim CoreJar As String = If(Config("CoreJar")?.ToString, "")
        Dim LaunchArgsFile As String = If(Config("LaunchArgsFile")?.ToString, "")
        Dim Command As New StringBuilder
        Command.AppendLine("@echo off")
        Command.AppendLine("setlocal DisableDelayedExpansion")
        Command.AppendLine("chcp 65001 >nul")
        Command.AppendLine("cd /d ""%~dp0..""")
        Command.AppendLine("title " & EscapeBatchTitle(Config("Name")?.ToString))
        Command.Append("""" & EscapeBatchPath(JavaPath) & """")
        Command.Append($" -Xms{FormatServerMemory(Config("XmsMB").ToObject(Of Integer))}")
        Command.Append($" -Xmx{FormatServerMemory(Config("XmxMB").ToObject(Of Integer))}")
        If Not String.IsNullOrWhiteSpace(Config("JvmArgs").ToString) Then Command.Append(" " & Config("JvmArgs").ToString)
        If LaunchMode = "Args" Then
            Dim LaunchArgsPath As String = Path.Combine(Folder, LaunchArgsFile)
            If String.IsNullOrWhiteSpace(LaunchArgsFile) OrElse Not FileUtils.Exists(LaunchArgsPath) Then
                Throw New Exception("未找到 Forge/NeoForge 服务端启动参数文件，请重新安装或刷新服务器。")
            End If
            Command.Append(" @""" & EscapeBatchPath(LaunchArgsFile.Replace("\", "/")) & """")
        Else
            Dim CorePath As String = Path.Combine(Folder, CoreJar)
            If String.IsNullOrWhiteSpace(CoreJar) OrElse Not FileUtils.Exists(CorePath) Then
                Throw New Exception("未找到服务端核心 JAR。")
            End If
            Dim CoreArgument As String = If(Path.IsPathRooted(CoreJar), Path.GetFullPath(CoreJar), CoreJar.Replace("/", "\"))
            Command.Append(" -jar """ & EscapeBatchPath(CoreArgument) & """")
        End If
        If Config("NoGui").ToObject(Of Boolean) Then Command.Append(" nogui")
        If Not String.IsNullOrWhiteSpace(Config("ServerArgs").ToString) Then Command.Append(" " & Config("ServerArgs").ToString)
        Command.AppendLine()
        Command.AppendLine("echo.")
        Command.AppendLine("echo Server process ended. Exit code: %ERRORLEVEL%")
        Return Command.ToString()
    End Function

    Public Sub LaunchServerInTerminal(Folder As String, Config As JObject)
        Dim ScriptPath As String = Path.Combine(Folder, "PCL\start-server.cmd")
        Dim Command As String = BuildServerLaunchScript(Folder, Config)
        DirectoryUtils.Create(PathUtils.RemoveLastPart(ScriptPath))
        FileUtils.Write(ScriptPath, Command)
        Logger.Info($"已生成服务器启动脚本：{ScriptPath}{vbCrLf}{Command.TrimEnd()}")

        StartProcess(New ProcessStartInfo("cmd.exe", "/d /k call """ & EscapeBatchPath(ScriptPath) & """") With {
            .WorkingDirectory = Folder,
            .UseShellExecute = True,
            .WindowStyle = ProcessWindowStyle.Normal
        })
    End Sub

    Public Function ResolveServerJavaPath(Folder As String, JavaPath As String) As String
        JavaPath = If(JavaPath, "").Trim()
        If JavaPath = "" Then Throw New Exception("尚未设置服务端 Java。")
        If Path.IsPathRooted(JavaPath) Then
            If Not FileUtils.Exists(JavaPath) Then Throw New Exception("服务端 Java 不存在：" & JavaPath)
            Return JavaPath
        End If
        Dim RelativePath As String = Path.Combine(Folder, JavaPath)
        If FileUtils.Exists(RelativePath) Then Return RelativePath
        If JavaPath.Contains("\") OrElse JavaPath.Contains("/") OrElse JavaPath.Contains(" - ") Then
            Throw New Exception("服务端 Java 路径无效：" & JavaPath)
        End If
        Return JavaPath
    End Function

    Public Function FormatServerMemory(Megabytes As Integer) As String
        If Megabytes <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(Megabytes))
        If Megabytes >= 1024 AndAlso Megabytes Mod 1024 = 0 Then Return $"{Megabytes \ 1024}G"
        Return $"{Megabytes}M"
    End Function

    Public Function InstallServerCore(Folder As String, InstallerPath As String, JavaPath As String,
                                      CoreType As ServerCoreType, MinecraftVersion As String, LoaderVersion As String,
                                      Optional CancellationToken As CancellationToken = Nothing) As ServerLaunchInfo
        If Not FileUtils.Exists(InstallerPath) Then Throw New Exception("服务端安装器不存在：" & InstallerPath)
        If String.IsNullOrWhiteSpace(JavaPath) Then Throw New Exception("没有找到可用于安装服务端的 Java。")

        Dim Arguments As String
        Select Case CoreType
            Case ServerCoreType.Fabric
                Arguments = "-jar " & QuoteCommandLineArgument(InstallerPath) &
                            " server -mcversion " & QuoteCommandLineArgument(MinecraftVersion) &
                            " -loader " & QuoteCommandLineArgument(LoaderVersion) &
                            " -downloadMinecraft"
            Case ServerCoreType.Forge, ServerCoreType.NeoForge
                Arguments = "-jar " & QuoteCommandLineArgument(InstallerPath) & " --installServer"
            Case Else
                Throw New ArgumentOutOfRangeException(NameOf(CoreType))
        End Select

        Dim StartInfo As New ProcessStartInfo With {
            .FileName = JavaPath,
            .Arguments = Arguments,
            .WorkingDirectory = Folder,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Logger.Info($"运行服务端安装器：{JavaPath} {Arguments}")
        Using Process = StartProcess(StartInfo)
            Dim OutputTask = Process.StandardOutput.ReadToEndAsync()
            Dim ErrorTask = Process.StandardError.ReadToEndAsync()
            Do While Not Process.WaitForExit(250)
                If CancellationToken.IsCancellationRequested Then
                    Try
                        If Not Process.HasExited Then Process.Kill()
                    Catch
                    End Try
                    Try
                        Process.WaitForExit(5000)
                    Catch
                    End Try
                    Throw New OperationCanceledException("服务端安装已取消。")
                End If
            Loop
            Dim OutputText As String = OutputTask.Result
            Dim ErrorText As String = ErrorTask.Result
            If Process.ExitCode <> 0 Then
                Dim Detail As String = If(String.IsNullOrWhiteSpace(ErrorText), OutputText, ErrorText).Trim
                If Detail.Length > 4000 Then Detail = Detail.Substring(Detail.Length - 4000)
                Throw New Exception($"服务端安装器退出码为 {Process.ExitCode}。{vbCrLf}{Detail}")
            End If
        End Using

        Dim LaunchInfo = FindServerLaunchInfo(Folder, CoreType)
        If LaunchInfo.LaunchMode = "Args" AndAlso Not String.IsNullOrWhiteSpace(LaunchInfo.LaunchArgsFile) Then Return LaunchInfo
        If Not String.IsNullOrWhiteSpace(LaunchInfo.CoreJar) Then Return LaunchInfo
        Throw New Exception("服务端安装器已结束，但没有找到可用的启动核心或启动参数文件。")
    End Function

    Private Function EscapeBatchPath(Value As String) As String
        Return Value.Replace("%", "%%")
    End Function

    Private Function EscapeBatchTitle(Value As String) As String
        Value = If(Value, "").Replace("""", "'").Replace("^", "^^").Replace("%", "%%").
            Replace("&", "^&").Replace("|", "^|").Replace("<", "^<").Replace(">", "^>").
            Replace("(", "^(").Replace(")", "^)")
        Return If(String.IsNullOrWhiteSpace(Value), "Minecraft Server", Value)
    End Function

End Module
