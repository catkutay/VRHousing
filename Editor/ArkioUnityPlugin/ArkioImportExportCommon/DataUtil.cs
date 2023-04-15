using System.Collections.Generic;

namespace ArkioImportExportCommon
{
    /** Utility class with some useful methods.
     */
    public static class DataUtil
    {
        /** Get minimum value in an array of ints. */
        public static int Min(int[] values)
        {
            int min = int.MaxValue;
            foreach (int val in values)
            {
                if (val < min) min = val;
            }
            return min;
        }

        public static int Min(List<int> values)
        {
            return Min(values.ToArray());
        }

        /** Get max valu in an array of ints. */
        public static int Max(int[] values)
        {
            int max = int.MinValue;
            foreach (int val in values)
            {
                if (val > max) max = val;
            }
            return max;
        }

        public static int Max(List<int> values)
        {
            return Max(values.ToArray());
        }

        /** Get minimum valu in an array of doubles. */
        public static double Min(double[] values)
        {
            double min = double.PositiveInfinity;
            foreach (double val in values)
            {
                if (val < min) min = val;
            }
            return min;
        }
    }
}

