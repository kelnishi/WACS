// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;

namespace Wacs.Core.Runtime
{
    [Flags]
    public enum InstructionLogging
    {
        None = 0,
        Calls = 1 << 0,
        Blocks = 1 << 1,
        Branches = 1 << 2,
        Computes = 1 << 3,
        Binds = 1 << 4,
        
        Control = Calls | Blocks | Branches | Binds,
        All = Calls | Blocks | Branches | Computes | Binds,
    }
    
    public class InvokerOptions
    {
        public bool CalculateLineNumbers = false;

        public bool CollectStats = false;
        public long GasLimit = 0;
        public bool LogGas = false;
        public InstructionLogging LogInstructionExecution = InstructionLogging.None;
        public int LogProgressEvery = -1;
        public bool ShowPath = false;

        public bool UseFastPath()
        {
            if (LogInstructionExecution != InstructionLogging.None)
                return false;
            if (LogGas)
                return false;
            if (LogProgressEvery > 0)
                return false;
            if (ShowPath)
                return false;
            if (CollectStats)
                return false;
            return true;
        }
    }
}