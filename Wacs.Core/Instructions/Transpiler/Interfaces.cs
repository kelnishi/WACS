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

using System;
using Wacs.Core.Runtime;

namespace Wacs.Core.Instructions.Transpiler
{
    public interface InstructionBaseAnalog
    {
        public int CalculateSize();
        public int LinkStackDiff { get; }
    }
    

    public interface IConvertableValueProducer {}

    public interface ITypedValueProducer<out T> : InstructionBaseAnalog, IConvertableValueProducer
    {
        public Func<ExecContext, T> GetFunc { get; }
    }

    public interface IOptimizationTarget
    {
        public int LinkStackDiff { get; }
    }

    public interface IValueConsumer<TIn1> {}
    public interface IValueConsumer<TIn1, TIn2> {}
    public interface IValueConsumer<TIn1, TIn2, TIn3> {}

    public interface INodeConsumer<TIn1> : IValueConsumer<TIn1>, IOptimizationTarget
    {
        public Action<ExecContext, TIn1> GetFunc { get; }
    }
    public interface INodeConsumer<TIn1, TIn2> : IValueConsumer<TIn1, TIn2>, IOptimizationTarget
    {
        public Action<ExecContext, TIn1, TIn2> GetFunc { get; }
    }
    
    public interface INodeComputer<TIn1, out TOut> : IValueConsumer<TIn1>, IOptimizationTarget
    {
        public Func<ExecContext, TIn1, TOut> GetFunc { get; }
    }
    
    public interface INodeComputer<TIn1, TIn2, out TOut> : IValueConsumer<TIn1, TIn2>, IOptimizationTarget
    {
        public Func<ExecContext, TIn1, TIn2, TOut> GetFunc { get; }
    }

    public interface INodeComputer<TIn1, TIn2, TIn3, out TOut> : IValueConsumer<TIn1, TIn2, TIn3>, IOptimizationTarget
    {
        public Func<ExecContext, TIn1, TIn2, TIn3, TOut> GetFunc { get; }
    }
    
}