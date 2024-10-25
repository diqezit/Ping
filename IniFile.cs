using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PingTestTool
{
    /// <summary>
    /// Класс для работы с INI-файлами.
    /// </summary>
    public class IniFile
    {
        #region Приватные поля

        private string path;

        #endregion

        #region Импорт функций из kernel32.dll

        /// <summary>
        /// Записывает строку в INI-файл.
        /// </summary>
        /// <param name="section">Секция.</param>
        /// <param name="key">Ключ.</param>
        /// <param name="val">Значение.</param>
        /// <param name="filePath">Путь к файлу.</param>
        /// <returns>Результат операции.</returns>
        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        /// <summary>
        /// Читает строку из INI-файла.
        /// </summary>
        /// <param name="section">Секция.</param>
        /// <param name="key">Ключ.</param>
        /// <param name="def">Значение по умолчанию.</param>
        /// <param name="retVal">Буфер для возвращаемого значения.</param>
        /// <param name="size">Размер буфера.</param>
        /// <param name="filePath">Путь к файлу.</param>
        /// <returns>Количество символов, скопированных в буфер.</returns>
        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="IniFile"/>.
        /// </summary>
        /// <param name="iniPath">Путь к INI-файлу.</param>
        public IniFile(string iniPath)
        {
            path = new FileInfo(iniPath).FullName;
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Записывает значение в INI-файл.
        /// </summary>
        /// <param name="section">Секция.</param>
        /// <param name="key">Ключ.</param>
        /// <param name="value">Значение.</param>
        public void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, path);
        }

        /// <summary>
        /// Читает значение из INI-файла.
        /// </summary>
        /// <param name="section">Секция.</param>
        /// <param name="key">Ключ.</param>
        /// <returns>Значение, считанное из INI-файла.</returns>
        public string Read(string section, string key)
        {
            var temp = new StringBuilder(255);
            int i = GetPrivateProfileString(section, key, "", temp, 255, path);
            return temp.ToString();
        }

        #endregion
    }
}