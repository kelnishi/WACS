// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>WIT <c>()</c> — the unit type. Used as a
    /// type-parameter witness for <c>result&lt;_, _&gt;</c>,
    /// <c>option&lt;_&gt;</c> placeholders, and unit-returning
    /// host methods that participate in canonical-ABI wire
    /// shapes the spec encodes.</summary>
    public readonly struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Value = default;
        public bool Equals(Unit other) => true;
        public override bool Equals(object? obj) => obj is Unit;
        public override int GetHashCode() => 0;
        public override string ToString() => "()";
    }

    /// <summary>Faithful WIT <c>option&lt;T&gt;</c>. Distinct
    /// from C#'s <c>T?</c> because canonical ABI treats option as
    /// a 2-case variant — the host code sees the discriminant
    /// directly. Use <see cref="Some(T)"/> / <see cref="None"/>
    /// to construct, and <see cref="HasValue"/> /
    /// <see cref="TryGetValue"/> to inspect.</summary>
    public readonly struct Option<T> : IEquatable<Option<T>>
    {
        private readonly T _value;
        public bool HasValue { get; }
        public T Value => HasValue ? _value
            : throw new InvalidOperationException(
                "Option<T> is None.");

        public static Option<T> None => default;
        public static Option<T> Some(T value) => new(value, true);

        private Option(T value, bool hasValue)
        {
            _value = value;
            HasValue = hasValue;
        }

        public bool TryGetValue(out T value)
        {
            value = _value;
            return HasValue;
        }

        public bool Equals(Option<T> other)
        {
            if (HasValue != other.HasValue) return false;
            if (!HasValue) return true;
            return Equals(_value, other._value);
        }

        public override bool Equals(object? obj) =>
            obj is Option<T> o && Equals(o);
        public override int GetHashCode() =>
            HasValue ? (_value?.GetHashCode() ?? 0) : 0;
        public override string ToString() =>
            HasValue ? $"Some({_value})" : "None";
    }

    /// <summary>Faithful WIT <c>result&lt;TOk, TErr&gt;</c>. A
    /// 2-case variant the host implements method by returning
    /// either branch, and observes on incoming params by checking
    /// <see cref="IsOk"/>. Throwing from a host method is a
    /// separate concern (becomes a wasm trap); use
    /// <see cref="FromErr"/> for non-trap Err propagation.</summary>
    public readonly struct Result<TOk, TErr> : IEquatable<Result<TOk, TErr>>
    {
        private readonly TOk _ok;
        private readonly TErr _err;
        public bool IsOk { get; }
        public bool IsErr => !IsOk;
        public TOk Ok => IsOk ? _ok
            : throw new InvalidOperationException(
                "Result is Err; cannot read Ok.");
        public TErr Err => !IsOk ? _err
            : throw new InvalidOperationException(
                "Result is Ok; cannot read Err.");

        public static Result<TOk, TErr> FromOk(TOk ok) =>
            new(ok, default!, true);
        public static Result<TOk, TErr> FromErr(TErr err) =>
            new(default!, err, false);

        private Result(TOk ok, TErr err, bool isOk)
        {
            _ok = ok;
            _err = err;
            IsOk = isOk;
        }

        public bool TryGetOk(out TOk ok)
        {
            ok = _ok;
            return IsOk;
        }

        public bool TryGetErr(out TErr err)
        {
            err = _err;
            return !IsOk;
        }

        public bool Equals(Result<TOk, TErr> other)
        {
            if (IsOk != other.IsOk) return false;
            return IsOk
                ? Equals(_ok, other._ok)
                : Equals(_err, other._err);
        }

        public override bool Equals(object? obj) =>
            obj is Result<TOk, TErr> r && Equals(r);
        public override int GetHashCode() =>
            IsOk ? (_ok?.GetHashCode() ?? 0)
                 : (_err?.GetHashCode() ?? 0);
        public override string ToString() =>
            IsOk ? $"Ok({_ok})" : $"Err({_err})";
    }
}
