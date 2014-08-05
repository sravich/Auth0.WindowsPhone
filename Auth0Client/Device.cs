using System;

namespace Auth0.SDK
{
    public class Device
    {
        private static string id;

        public static string GetUniqueId()
        {
#if WINDOWS_PHONE
            return Guid.NewGuid().ToString();
#else

            if (string.IsNullOrEmpty(id))
            {
                var token = Windows.System.Profile.HardwareIdentification.GetPackageSpecificToken(null);
                var hardwareId = token.Id;
                byte[] bytes = new byte[hardwareId.Length];
                using (var dataReader = Windows.Storage.Streams.DataReader.FromBuffer(hardwareId))
                {    
                    dataReader.ReadBytes(bytes);
                    id = BitConverter.ToString(bytes).Replace("-","");
                }
             }

            return id;
#endif
        }
    }
}
