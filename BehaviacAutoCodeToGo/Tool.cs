using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace behaviac_autoCodeToGo
{
    public enum TypeKind
    {
        Type,
        Agent,
        Enum
    }


    public static class Tool
    {
        public static List<string> customAgentClassNames = new List<string>();

        public static void Init(XmlDocument doc)
        {
            XmlNodeList list = doc.GetElementsByTagName("agent");
            var enumerator = list.GetEnumerator();
            while (enumerator.MoveNext())
            {
                string className = (enumerator.Current as XmlNode).Attributes["classfullname"].InnerText;
                if (className == "behaviac::Agent") continue;
                customAgentClassNames.Add(className);
            }
        }

        public static Action<string> CleanOldCpp(List<string> lineCodes, string startFlag)
        {
            int initStartLine = lineCodes.IndexOf(startFlag) + 1;
            while (lineCodes[initStartLine] != "///<<< END WRITING YOUR CODE")
                lineCodes.RemoveAt(initStartLine);
            return code => { lineCodes.Insert(initStartLine++, code); };
        }

        public static TypeKind GetAttributeTypeKind(XmlNode node, out string type, out bool isString, bool isRet = false, bool isCstr = true)
        {
            string fullAttName = isRet ? "ReturnTypeFullName" : "TypeFullName";
            string attName = isRet ? "ReturnType" : "Type";

            string rtype = node.Attributes[attName].InnerText;
            if (rtype.EndsWith("&") || rtype.EndsWith("*"))
                rtype = rtype.Substring(0, rtype.Length - 1);
            if (rtype == "string")
            {
                type = isCstr ? "const char*" : "string";
                isString = true;
            }
            else
            {
                type = rtype;
                isString = false;
            }
            if (!node.Attributes[fullAttName].InnerText.StartsWith("XMLPluginBehaviac"))
                return TypeKind.Type;

            type = "int";
            if (customAgentClassNames.Contains(rtype))
                return TypeKind.Agent;
            return TypeKind.Enum;
        }

    }
}
