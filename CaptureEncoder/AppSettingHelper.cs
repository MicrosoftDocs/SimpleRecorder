using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace CaptureEncoder
{
    //public static class AppSettingHelper
    //{
    //    private static ApplicationDataContainer root = ApplicationData.Current.LocalSettings;

    //    public static string key_RecordMike = "IsMikeRecord";
    //    public static string key_InputDeviceIndex = "MikeRecordedIndex";
    //    public static string key_OutputDeviceRecordedIndex = "OutputDeviceRecordedIndex";

    //    public static bool UseMikeAsInput
    //    {
    //        get
    //        {
    //            return ReadSetting<bool>(key_RecordMike);

    //        }
    //    }

    //    public static int InputDeviceIndex
    //    {
    //        get
    //        {
    //            return ReadSetting<int>(key_InputDeviceIndex);

    //        }
    //    }

    //    public static int OutputDeviceRecordedIndex
    //    {
    //        get
    //        {
    //            return ReadSetting<int>(key_OutputDeviceRecordedIndex);

    //        }
    //    }



    //    // 
    //    public static T ReadSetting<T>(string key)
    //    {
    //        if (root.Values.TryGetValue(key, out object value))
    //        {
    //            return (T)value;
    //        }
    //        else
    //        {
    //            return default(T);
    //        }
    //    }

    //    // 
    //    public static void WriteSetting<T>(string key, T Tvalue)// where T : struct
    //    {
    //        ApplicationDataContainer root = ApplicationData.Current.LocalSettings;
    //        if (root.Values.TryGetValue(key, out object oldkey))
    //        {

    //            root.Values[key] = Tvalue;
    //        }
    //        else
    //        {
    //            root.Values.Add(key, Tvalue);
    //        }
    //    }

    //}
}
