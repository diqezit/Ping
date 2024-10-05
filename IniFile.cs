using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PingTestTool
{
    public class IniFile
    {
        private string path;

        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        public IniFile(string iniPath)
        {
            path = new FileInfo(iniPath).FullName;
        }

        public void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, path);
        }

        public string Read(string section, string key)
        {
            var temp = new StringBuilder(255);
            int i = GetPrivateProfileString(section, key, "", temp, 255, path);
            return temp.ToString();
        }
    }
}
