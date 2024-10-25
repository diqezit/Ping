using System;
using System.Collections.Generic;
using System.Linq;

namespace PingTestTool
{
    public class GraphDataManager
    {
        #region Приватные поля

        private List<int> pingData;
        private List<double> cachedSmoothedData;
        private bool isDataDirty = true;
        private int smoothingWindowSize = 5;

        #endregion

        #region Конструктор

        public GraphDataManager()
        {
            pingData = new List<int>();
        }

        #endregion

        #region Публичные методы

        public void SetPingData(List<int> data)
        {
            pingData = data;
            isDataDirty = true;
        }

        public List<double> GetDataToPlot(bool isSmoothingEnabled)
        {
            return isSmoothingEnabled ? ApplyMovingAverage(pingData, smoothingWindowSize) : pingData.Select(x => (double)x).ToList();
        }

        public (double Min, double Avg, double Max, double Cur) GetStatistics()
        {
            if (pingData.Count == 0)
            {
                return (double.NaN, double.NaN, double.NaN, double.NaN);
            }

            return (pingData.Min(), pingData.Average(), pingData.Max(), pingData.Last());
        }

        #endregion

        #region Приватные методы

        private List<double> ApplyMovingAverage(List<int> data, int windowSize)
        {
            if (!isDataDirty && cachedSmoothedData != null)
            {
                return cachedSmoothedData;
            }

            List<double> smoothedData = new List<double>(data.Count);
            double sum = 0;

            for (int i = 0; i < data.Count; i++)
            {
                sum += data[i];
                if (i >= windowSize) sum -= data[i - windowSize];
                smoothedData.Add(sum / Math.Min(i + 1, windowSize));
            }

            cachedSmoothedData = smoothedData;
            isDataDirty = false;
            return smoothedData;
        }

        #endregion
    }
}