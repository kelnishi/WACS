using System.Collections.Generic;

namespace Spec.Test.WastJson
{
    public interface IAction
    {
        ActionType Type { get; }

        string Field { get; set; }

        List<Argument> Args { get; set; }
    }
}