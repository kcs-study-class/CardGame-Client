using System;
using System.Collections.Generic;
using System.Threading;
using KTC.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityFramework.Boot;

namespace KTC.Poker.Tests
{
    public class BootPipelineTests
    {
        /// <summary>同期完了するテスト用タスク。</summary>
        private sealed class FakeTask : IBootTask
        {
            private readonly Func<BootContext, BootTaskResult> _run;
            public string DisplayName { get; }
            public int RunCount = 0;

            public FakeTask(string name, Func<BootContext, BootTaskResult> run = null)
            {
                DisplayName = name;
                _run = run;
            }

            public Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken)
            {
                RunCount++;
                AwaitableCompletionSource<BootTaskResult> source = new AwaitableCompletionSource<BootTaskResult>();
                try
                {
                    source.SetResult(_run != null ? _run(context) : BootTaskResult.Ok());
                }
                catch (Exception e)
                {
                    source.SetException(e);
                }
                return source.Awaitable;
            }
        }

        private static BootResult Run(BootPipeline pipeline, BootContext context = null)
        {
            Awaitable<BootResult>.Awaiter awaiter = pipeline.RunAsync(context != null ? context : new BootContext(), CancellationToken.None).GetAwaiter();
            Assert.That(awaiter.IsCompleted, Is.True, "テスト用タスクは同期完了する構成のはず");
            return awaiter.GetResult();
        }

        [Test]
        public void 登録順に全タスクが実行される()
        {
            List<string> order = new List<string>();
            BootPipeline pipeline = new BootPipeline(new IBootTask[]
            {
                new FakeTask("A", _ => { order.Add("A"); return BootTaskResult.Ok(); }),
                new FakeTask("B", _ => { order.Add("B"); return BootTaskResult.Ok(); }),
                new FakeTask("C", _ => { order.Add("C"); return BootTaskResult.Ok(); }),
            });
            List<string> events = new List<string>();
            pipeline.TaskStarted += (i, total, name) => events.Add($"{i + 1}/{total}:{name}");

            BootResult result = Run(pipeline);
            Assert.That(result.Success, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(events, Is.EqualTo(new[] { "1/3:A", "2/3:B", "3/3:C" }));
        }

        [Test]
        public void 失敗タスクで停止し後続は実行されない()
        {
            FakeTask taskC = new FakeTask("C");
            BootPipeline pipeline = new BootPipeline(new IBootTask[]
            {
                new FakeTask("A"),
                new FakeTask("B", _ => BootTaskResult.Fail("サーバーに接続できません")),
                taskC,
            });

            BootResult result = Run(pipeline);
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailedTaskName, Is.EqualTo("B"));
            Assert.That(result.Message, Is.EqualTo("サーバーに接続できません"));
            Assert.That(taskC.RunCount, Is.Zero, "失敗以降のタスクは走らない");
        }

        [Test]
        public void リトライは失敗タスクから再開する()
        {
            int attempts = 0;
            FakeTask taskA = new FakeTask("A");
            BootPipeline pipeline = new BootPipeline(new IBootTask[]
            {
                taskA,
                new FakeTask("B", _ => ++attempts < 2 ? BootTaskResult.Fail("一時エラー") : BootTaskResult.Ok()),
                new FakeTask("C"),
            });

            Assert.That(Run(pipeline).Success, Is.False, "1回目はBで失敗");
            BootResult retry = Run(pipeline);
            Assert.That(retry.Success, Is.True, "2回目はBから再開して成功");
            Assert.That(taskA.RunCount, Is.EqualTo(1), "成功済みのAはやり直さない");
        }

        [Test]
        public void 例外はFail扱いになる()
        {
            BootPipeline pipeline = new BootPipeline(new IBootTask[]
            {
                new FakeTask("Crash", _ => throw new InvalidOperationException("boom")),
            });
            // SafeLogger のエラーログはこのテストでは想定内
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("BootPipeline.*boom"));
            BootResult result = Run(pipeline);
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailedTaskName, Is.EqualTo("Crash"));
        }

        [Test]
        public void コンテキストがタスク間で受け渡される()
        {
            BootPipeline pipeline = new BootPipeline(new IBootTask[]
            {
                new FakeTask("Login", c => { c.AuthToken = "token-123"; return BootTaskResult.Ok(); }),
                new FakeTask("UseToken", c => c.AuthToken == "token-123"
                    ? BootTaskResult.Ok()
                    : BootTaskResult.Fail("トークン未設定")),
            });
            Assert.That(Run(pipeline).Success, Is.True);
        }
    }
}
