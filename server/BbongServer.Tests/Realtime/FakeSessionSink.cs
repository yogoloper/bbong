using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Realtime;

namespace BbongServer.Tests.Realtime;

/// <summary>보낸 메시지를 객체 그대로 기록하는 테스트용 싱크.</summary>
public sealed class FakeSessionSink : ISessionSink
{
    public FakeSessionSink(Guid userId) => UserId = userId;

    public Guid UserId { get; }

    public List<object> Sent { get; } = new();

    public Task SendAsync(object message)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public IEnumerable<T> SentOf<T>() => Sent.OfType<T>();

    public T Last<T>() => Sent.OfType<T>().Last();
}
