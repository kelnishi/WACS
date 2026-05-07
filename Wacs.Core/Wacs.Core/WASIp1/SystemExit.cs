// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Wacs.Core.WASIp1
{
    public enum SystemExit : int
    {
        [Signal("Ok: Success")] Ok = 0,
        [Signal("Base exit status")] Base = 64,
        [Signal("Command line usage error")] Usage = 64,
        [Signal("Data format error")] DataErr = 65,
        [Signal("Input error")] NoInput = 66,
        [Signal("No user available")] NoUser = 67,
        [Signal("No host available")] NoHost = 68,
        [Signal("Service unavailable")] Unavailable = 69,
        [Signal("Software error")] Software = 70,
        [Signal("Operating system error")] OsErr = 71,
        [Signal("Critical OS file error")] OsFile = 72,
        [Signal("Cannot create output file")] CantCreat = 73,
        [Signal("I/O error")] IoErr = 74,
        [Signal("Temporary failure")] TempFail = 75,
        [Signal("Protocol error")] Protocol = 76,
        [Signal("Permission denied")] NoPerm = 77,
        [Signal("Configuration error")] Config = 78,
        
        _Max = 78
    }
    
    public static class SystemExitExtension
    {
        public static string HumanReadable(this SystemExit sig)
        {
            var type = typeof(ErrNo);
            var memberInfo = type.GetMember(sig.ToString());
            if (memberInfo.Length <= 0)
                return $"SystemExit: {sig}";

            var attributes = memberInfo[0].GetCustomAttributes(typeof(SignalAttribute), false);
            if (attributes.Length > 0)
            {
                return $"{sig}: {((SignalAttribute)attributes[0]).HumanReadableMessage}";
            }

            return $"SystemExit: {sig}";
        }
    }
}