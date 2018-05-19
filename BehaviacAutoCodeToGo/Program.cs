using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace behaviac_autoCodeToGo
{
    class Program
    {
        static string MetaPath = "G:/wproject_pokemon/trunk/Desginer/Plan/09行为树/behaviors/behaviac_meta/pkm.meta.xml";
        static string CppExportPath = "G:/wproject_pokemon/trunk/Server/behaviac_dll/gointerface/behaviac_generated/types/internal/";
        static string GoBridgePath = "G:/wproject_pokemon/trunk/Server/src/behaviac/";
        static string GoImportPath = "behaviac";

        static Dictionary<string, string> typeErrorValue = new Dictionary<string, string>();

        static void Main(string[] args)
        {
            if (args.Length > 0) MetaPath = args[0];
            if (args.Length > 1) CppExportPath = args[1] + "/";
            if (args.Length > 2) GoBridgePath = args[2] + "/";
            if (args.Length > 3) GoImportPath = args[3];

            typeErrorValue.Add("string", "\"\"");
            typeErrorValue.Add("const char*", "\"\"");
            typeErrorValue.Add("bool", "false");
            typeErrorValue.Add("int", "0");
            typeErrorValue.Add("float64", "0");
            typeErrorValue.Add("double", "0");
            typeErrorValue.Add("", "");

            try
            {
                // 读取xml
                XmlDocument doc = new XmlDocument();
                doc.Load(MetaPath);
                Tool.Init(doc);

                // 写internal.i
                StringBuilder internalsb = new StringBuilder();
                internalsb.AppendLine("%module(directors=\"1\") internal");
                internalsb.AppendLine("%{");
                internalsb.AppendLine("#include \"AgentBase.h\"");
                Tool.customAgentClassNames.ForEach(className => { internalsb.AppendLine("#include \"Agent" + className + ".h\""); });
                internalsb.AppendLine("%}");
                internalsb.AppendLine();
                Tool.customAgentClassNames.ForEach(className => { internalsb.AppendLine("%feature(\"director\") Agent" + className + "Funcs;"); });
                internalsb.AppendLine();
                internalsb.AppendLine("%include \"AgentBase.h\"");
                Tool.customAgentClassNames.ForEach(className => { internalsb.AppendLine("%include \"Agent" + className + ".h\""); });
                File.WriteAllText(GoBridgePath + "internal/internal.i", internalsb.ToString());
                Console.WriteLine("writing internal.i ...");

                //ProcessStartInfo gofmt
                ProcessStartInfo gofmtStartInfo = new ProcessStartInfo();
                gofmtStartInfo.FileName = "gofmt";
                gofmtStartInfo.UseShellExecute = false;
                gofmtStartInfo.CreateNoWindow = false;

                // 写Go枚举
                var enumerator = doc.GetElementsByTagName("enumtype").GetEnumerator();
                StringBuilder goEnumsb = new StringBuilder();
                goEnumsb.AppendLine("package behaviac");
                goEnumsb.AppendLine("const (");
                while (enumerator.MoveNext())
                {
                    XmlNode node = enumerator.Current as XmlNode;
                    goEnumsb.AppendLine("//" + node.Attributes["DisplayName"].InnerText);
                    foreach (XmlNode enumNode in node.ChildNodes)
                    {
                        goEnumsb.AppendLine("E_" + node.Attributes["Type"].InnerText.ToUpper() + "_" + enumNode.Attributes["NativeValue"].InnerText.ToUpper() + "=" + enumNode.Attributes["MemberValue"].InnerText + " //" + enumNode.Attributes["DisplayName"].InnerText);
                    }
                    goEnumsb.AppendLine();
                }
                goEnumsb.AppendLine(")");
                string goEnumPath = GoBridgePath + "enum.go";
                File.WriteAllText(goEnumPath, goEnumsb.ToString());
                gofmtStartInfo.Arguments = "-w " + new FileInfo(goEnumPath).FullName;
                Process.Start(gofmtStartInfo).WaitForExit();
                Console.WriteLine("writing enum.go ...");

                // 写代码
                enumerator = doc.GetElementsByTagName("agent").GetEnumerator();
                while (enumerator.MoveNext())
                {
                    string codeType;
                    bool codeTypeIsString;
                    TypeKind kind;

                    XmlNode node = enumerator.Current as XmlNode;
                    string className = node.Attributes["classfullname"].InnerText;
                    if (className == "behaviac::Agent") continue;
                    Console.WriteLine("");

                    #region CPP
                    // 读取CPP
                    List<string> cppCodeLines = new List<string>(File.ReadAllLines(CppExportPath + className + ".cpp"));

                    // init头
                    // 清理init段的代码
                    Action<string> appendCodeLine = Tool.CleanOldCpp(cppCodeLines, "///<<< BEGIN WRITING YOUR CODE FILE_INIT");
                    // 头文件引用
                    appendCodeLine("#include \"bridge.h\"");
                    appendCodeLine("using namespace behaviac;");
                    appendCodeLine(string.Empty);
                    string funcs_ptr = "", line;
                    List<string> funcs_ptr_name = new List<string>();
                    // 自定义函数指针
                    foreach (XmlNode methodNode in node.ChildNodes)
                    {
                        if (methodNode.LocalName != "Method") continue;
                        string paramStr = "";
                        foreach (XmlNode paramNode in methodNode.ChildNodes)
                        {
                            kind = Tool.GetAttributeTypeKind(paramNode, out codeType, out codeTypeIsString);
                            paramStr += "," + codeType + " " + paramNode.Attributes["Name"].InnerText;
                        }
                        kind = Tool.GetAttributeTypeKind(methodNode, out codeType, out codeTypeIsString, true);
                        line = codeType + "(*Agent_" + className + "_" + methodNode.Attributes["Name"].InnerText + ")(int agentid" + paramStr + ");";
                        appendCodeLine(line); funcs_ptr += line; funcs_ptr_name.Add("Agent_" + className + "_" + methodNode.Attributes["Name"].InnerText);
                    }
                    // 公开给C的接口
                    appendCodeLine("extern \"C\"{");
                    // 创建Agent
                    appendCodeLine("EXPORT int Agent_" + className + "_Create() {");
                    appendCodeLine("behaviac::Agent* agent = behaviac::Agent::Create<" + className + ">();");
                    appendCodeLine("allAgentInstances.insert(std::map<int, void*>::value_type(agent->GetId(), agent));");
                    appendCodeLine("return agent->GetId();");
                    appendCodeLine("}");
                    // 自定义属性访问
                    foreach (XmlNode memberNode in node.ChildNodes)
                    {
                        if (memberNode.LocalName != "Member") continue;
                        kind = Tool.GetAttributeTypeKind(memberNode, out codeType, out codeTypeIsString);
                        // get
                        appendCodeLine("EXPORT " + codeType + " Agent_" + className + "_Get" + memberNode.Attributes["Name"].InnerText + "(int agentid) {");
                        appendCodeLine(className + "* agent = (" + className + "*)allAgentInstances[agentid];");
                        appendCodeLine("if (agent == NULL){");
                        appendCodeLine("LogManager::GetInstance()->Log(\"agent has been destroyed, id = %i\", agentid);");
                        appendCodeLine("return " + (kind == TypeKind.Agent ? "-1" : typeErrorValue[codeType]) + ";");
                        appendCodeLine("}");
                        if (kind == TypeKind.Agent)
                            appendCodeLine("return agent->" + memberNode.Attributes["Name"].InnerText + " == nullptr ? -1 : agent->" + memberNode.Attributes["Name"].InnerText + "->GetId();");
                        else
                            appendCodeLine("return agent->" + memberNode.Attributes["Name"].InnerText + (codeTypeIsString ? ".c_str()" : "") + ";");
                        appendCodeLine("}");
                        // set
                        appendCodeLine("EXPORT void Agent_" + className + "_Set" + memberNode.Attributes["Name"].InnerText + "(int agentid, " + codeType + " value) {");
                        appendCodeLine(className + "* agent = (" + className + "*)allAgentInstances[agentid];");
                        appendCodeLine("if (agent == NULL){");
                        appendCodeLine("LogManager::GetInstance()->Log(\"agent has been destroyed, id = %i\", agentid);");
                        appendCodeLine("return;");
                        appendCodeLine("}");
                        if (kind == TypeKind.Agent)
                            appendCodeLine("agent->" + memberNode.Attributes["Name"].InnerText + " = (" + memberNode.Attributes["Type"].InnerText + ")allAgentInstances[value];");
                        else
                            appendCodeLine("agent->" + memberNode.Attributes["Name"].InnerText + " = (" + memberNode.Attributes["Type"].InnerText + ")value;");
                        appendCodeLine("}");
                    }
                    // 函数指针赋值方法
                    appendCodeLine("EXPORT void Agent_" + className + "_RegFuncs(" + (funcs_ptr.Replace("*Agent_", "*_Agent_").Replace(";", ",").TrimEnd(',')) + "){");
                    funcs_ptr_name.ForEach(name => { appendCodeLine(name + " = _" + name + ";"); });
                    appendCodeLine("}");
                    // 公开给C的接口 结束
                    appendCodeLine("}");

                    // 为自定义方法添加调用
                    foreach (XmlNode methodNode in node.ChildNodes)
                    {
                        if (methodNode.LocalName != "Method") continue;
                        string methodName = methodNode.Attributes["Name"].InnerText;
                        appendCodeLine = Tool.CleanOldCpp(cppCodeLines, "///<<< BEGIN WRITING YOUR CODE " + methodName);
                        string paramStr = "";
                        foreach (XmlNode paramNode in methodNode.ChildNodes)
                        {
                            kind = Tool.GetAttributeTypeKind(paramNode, out codeType, out codeTypeIsString);
                            if (kind == TypeKind.Agent)
                                paramStr += "," + paramNode.Attributes["Name"].InnerText + ".GetId()";
                            else
                                paramStr += "," + paramNode.Attributes["Name"].InnerText + (codeTypeIsString ? ".c_str()" : "");
                        }
                        string ReturnType = methodNode.Attributes["ReturnType"].InnerText;
                        appendCodeLine((ReturnType != "void" ? "return (" + ReturnType + ") " : "") + "Agent_" + className + "_" + methodName + "(this==NULL?-1:this->GetId()" + paramStr + ");");
                    }

                    // 写入CPP
                    File.WriteAllLines(CppExportPath + className + ".cpp", cppCodeLines.ToArray(), Encoding.UTF8);
                    Console.WriteLine("writing " + className + ".cpp ...");
                    #endregion

                    #region SWIG
                    StringBuilder swigsb = new StringBuilder();
                    swigsb.AppendLine("extern \"C\" {");
                    // 属性访问
                    foreach (XmlNode memberNode in node.ChildNodes)
                    {
                        if (memberNode.LocalName != "Member") continue;
                        kind = Tool.GetAttributeTypeKind(memberNode, out codeType, out codeTypeIsString);
                        swigsb.AppendLine("extern " + codeType + " Agent_" + className + "_Get" + memberNode.Attributes["Name"].InnerText + "(int agentid);");
                        swigsb.AppendLine("extern void Agent_" + className + "_Set" + memberNode.Attributes["Name"].InnerText + "(int agentid, " + codeType + " value);");
                    }
                    // 注册Go实现的接口
                    swigsb.AppendLine("extern void Agent_" + className + "_RegFuncs(" + (funcs_ptr.Replace("string ", "const char* ").Replace("*Agent_", "*_Agent_").Replace(";", ",").TrimEnd(',')) + ");");
                    // 创建Agent
                    swigsb.AppendLine("extern int Agent_" + className + "_Create();");
                    swigsb.AppendLine("}");
                    swigsb.AppendLine();
                    // 接口调用虚类
                    swigsb.AppendLine("class Agent" + className + "Funcs {");
                    swigsb.AppendLine("public:");
                    funcs_ptr.Replace("string ", "const char* ").Split(';').ToList<string>().ForEach(code =>
                    {
                        if (string.IsNullOrEmpty(code)) return;
                        swigsb.AppendLine("virtual " + code.Replace("(*Agent_" + className + "_", " Agent" + className + "_").Replace(")(", "(") + (code.StartsWith("int(") ? "{ return 0; }" : "{}"));
                    });
                    swigsb.AppendLine("virtual ~Agent" + className + "Funcs(){}");
                    swigsb.AppendLine("};");
                    // 接口Go实现的实例
                    List<string> funcNames = new List<string>();
                    swigsb.AppendLine("static Agent" + className + "Funcs* __" + className + "_funcs_ins;");
                    funcs_ptr.Replace("string ", "const char* ").Split(';').ToList<string>().ForEach(code =>
                     {
                         if (string.IsNullOrEmpty(code)) return;
                         string paramStr = code.Substring(code.IndexOf(")(") + 1);
                         int funcStartIndex = code.IndexOf("(*Agent_" + className) + 2;
                         string funcName = code.Substring(funcStartIndex, code.Length - paramStr.Length - funcStartIndex - 1);
                         paramStr = paramStr.Substring(1, paramStr.Length - 2);
                         paramStr = string.Join(",", paramStr.Split(',').Select(s => { return s.Split(' ')[s.Split(' ').Length - 1]; }));
                         swigsb.AppendLine(code.Replace("(*Agent_" + className + "_", " __Agent_" + className + "_").Replace(")(", "(") + "{" + (code.StartsWith("void(") ? "" : "return ") +
                             "__" + className + "_funcs_ins->Agent" + funcName.Substring(6) + "(" + paramStr + ");}");
                         funcNames.Add(funcName);
                     });
                    swigsb.AppendLine("void SetAgent" + className + "Funcs(Agent" + className + "Funcs* funcs) {");
                    swigsb.AppendLine("__" + className + "_funcs_ins = funcs;");
                    appendCodeLine("EXPORT void Agent_" + className + "_RegFuncs(" + (funcs_ptr.Replace("*Agent_", "*_Agent_").Replace(";", ",").TrimEnd(',')) + "){");
                    swigsb.AppendLine("Agent_" + className + "_RegFuncs(" + string.Join(",", funcNames.Select(s => { return "__" + s; })) + ");");
                    swigsb.AppendLine("}");


                    File.WriteAllText(GoBridgePath + "internal/Agent" + className + ".h", swigsb.ToString());
                    Console.WriteLine("writing internal/Agent" + className + ".h ...");
                    #endregion

                    #region go
                    funcs_ptr = funcs_ptr.Replace("float", "float32").Replace("double", "float64");

                    StringBuilder gosb = new StringBuilder();
                    gosb.AppendLine("package behaviac");
                    gosb.AppendLine("import \"" + GoImportPath + "/internal\"");
                    gosb.AppendLine("import \"github.com/name5566/leaf/log\"");
                    gosb.AppendLine("import \"runtime/debug\"");

                    // 重写Funcs的极口
                    gosb.AppendLine("type Agent" + className + "Overwrite interface {");
                    funcs_ptr.Split(';').ToList<string>().ForEach(code =>
                    {
                        if (string.IsNullOrEmpty(code)) return;
                        string paramStr = code.Substring(code.IndexOf(")(") + 1);
                        int funcStartIndex = code.IndexOf("(*Agent_" + className) + 2;
                        string funcName = code.Substring(funcStartIndex, code.Length - paramStr.Length - funcStartIndex - 1);
                        paramStr = paramStr.Substring(1, paramStr.Length - 2);
                        paramStr = string.Join(",", paramStr.Split(',').Select(s =>
                        {
                            int index = s.LastIndexOf(' '); string t = s.Substring(0, index);
                            return s.Substring(index) + " " + (t == "const char*" ? "string" : t);
                        }));
                        string retStr = code.Substring(0, code.IndexOf("(*"));
                        gosb.AppendLine("Agent" + funcName.Substring(6) + "(" + paramStr + ") " + (retStr == "void" ? "" : retStr));
                    });
                    gosb.AppendLine("}");
                    gosb.AppendLine("var agent" + className + "OverwriteIns Agent" + className + "Overwrite");
                    gosb.AppendLine("type instanceAgent" + className + "Funcs struct {");
                    gosb.AppendLine("internal.Agent" + className + "Funcs");
                    gosb.AppendLine("}");
                    gosb.AppendLine("func RegAgent" + className + "Func(funcs Agent" + className + "Overwrite){");
                    gosb.AppendLine("agent" + className + "OverwriteIns = funcs");
                    gosb.AppendLine("internal.SetAgent" + className + "Funcs(&instanceAgent" + className + "Funcs{Agent" + className + "Funcs: internal.NewDirectorAgent" + className + "Funcs(agent" + className + "OverwriteIns)})");
                    gosb.AppendLine("}");
                    // 对应的Agent类和创建
                    gosb.AppendLine("type Agent" + className + " struct {");
                    gosb.AppendLine("*AgentBase");
                    gosb.AppendLine("}");
                    gosb.AppendLine("func NewAgent" + className + "Inherit(id int) *Agent" + className + " {");
                    gosb.AppendLine("return &Agent" + className + "{NewAgentBaseInherit(id)}");
                    gosb.AppendLine("}");
                    gosb.AppendLine("func NewAgent" + className + "() *Agent" + className + " {");
                    gosb.AppendLine("var id int");
                    gosb.AppendLine("exeOnMain(func() {");
                    gosb.AppendLine("id = internal.Agent_" + className + "_Create()");
                    gosb.AppendLine("})");
                    gosb.AppendLine("agent := NewAgent" + className + "Inherit(id)");
                    gosb.AppendLine("allGoAgentInstances.Store(id, agent)");
                    gosb.AppendLine("return agent");
                    gosb.AppendLine("}");
                    // Agent类的属性读写
                    foreach (XmlNode memberNode in node.ChildNodes)
                    {
                        if (memberNode.LocalName != "Member") continue;
                        kind = Tool.GetAttributeTypeKind(memberNode, out codeType, out codeTypeIsString, false, false);
                        codeType = codeType == "float" ? "float32" : (codeType == "double" ? "float64" : codeType);
                        string goName = memberNode.Attributes["Name"].InnerText.Substring(0, 1).ToUpper() + memberNode.Attributes["Name"].InnerText.Substring(1);
                        gosb.AppendLine("func (ins *Agent" + className + ") Get" + goName + "() " + codeType + "{");
                        gosb.AppendLine("if ins.m_id < 0 {");
                        gosb.AppendLine("log.Error(\"agent has been destroyed %s\", debug.Stack())");
                        gosb.AppendLine("return " + typeErrorValue[codeType]);
                        gosb.AppendLine("}");
                        gosb.AppendLine("return internal.Agent_" + className + "_Get" + memberNode.Attributes["Name"].InnerText + "(ins.m_id)");
                        gosb.AppendLine("}");
                        gosb.AppendLine("func (ins *Agent" + className + ") Set" + goName + "(value " + codeType + " ) {");
                        gosb.AppendLine("if ins.m_id < 0 {");
                        gosb.AppendLine("log.Error(\"agent has been destroyed %s\", debug.Stack())");
                        gosb.AppendLine("return");
                        gosb.AppendLine("}");
                        gosb.AppendLine("internal.Agent_" + className + "_Set" + memberNode.Attributes["Name"].InnerText + "(ins.m_id, value)");
                        gosb.AppendLine("}");
                    }
                    // Agent类的方法
                    foreach (XmlNode methodNode in node.ChildNodes)
                    {
                        if (methodNode.LocalName != "Method") continue;
                        string methodName = methodNode.Attributes["Name"].InnerText;
                        string paramCallStr = "", paramTypeStr = "";
                        foreach (XmlNode paramNode in methodNode.ChildNodes)
                        {
                            kind = Tool.GetAttributeTypeKind(paramNode, out codeType, out codeTypeIsString, false, false);
                            codeType = codeType == "float" ? "float32" : (codeType == "double" ? "float64" : codeType);
                            string paramName = paramNode.Attributes["Name"].InnerText + (kind == TypeKind.Agent ? "Id" : "");
                            paramTypeStr += "," + paramName + " " + codeType;
                            paramCallStr += "," + paramName;
                        }
                        if (paramTypeStr.Length > 0) paramTypeStr = paramTypeStr.Substring(1);
                        kind = Tool.GetAttributeTypeKind(methodNode, out codeType, out codeTypeIsString, true, false);
                        codeType = codeType == "void" ? "" : codeType;
                        codeType = codeType == "float" ? "float32" : (codeType == "double" ? "float64" : codeType);
                        gosb.AppendLine("func (ins *Agent" + className + ") " + methodName.Substring(0, 1).ToUpper() + methodName.Substring(1) + "(" + paramTypeStr + ") " + codeType + " {");
                        gosb.AppendLine("if ins.m_id < 0 {");
                        gosb.AppendLine("log.Error(\"agent has been destroyed %s\", debug.Stack())");
                        gosb.AppendLine("return " + typeErrorValue[codeType]);
                        gosb.AppendLine("}");
                        gosb.AppendLine((string.IsNullOrEmpty(codeType) ? "" : "return ") + "agent" + className + "OverwriteIns.Agent" + className + "_" + methodName + "(ins.m_id" + paramCallStr + ")");
                        gosb.AppendLine("}");
                    }
                    // 写入文件
                    string goAgentPath = GoBridgePath + "agent_" + className.ToLower() + ".go";
                    File.WriteAllText(goAgentPath, gosb.ToString());
                    gofmtStartInfo.Arguments = "-w " + new FileInfo(goAgentPath).FullName;
                    Process.Start(gofmtStartInfo).WaitForExit();
                    Console.WriteLine("writing Agent" + className + ".go ...");
                    #endregion
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
#if DEBUG
                Console.ReadKey();
#endif
            }

            Console.WriteLine("complete!");
        }
    }
}
