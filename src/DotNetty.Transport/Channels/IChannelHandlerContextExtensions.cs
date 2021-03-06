﻿namespace DotNetty.Transport.Channels
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using DotNetty.Common;
    using DotNetty.Common.Utilities;

    public static class IChannelHandlerContextExtensions
    {
        public static Task WriteAndFlushManyAsync(this IChannelHandlerContext context, params object[] msgs) => WriteAndFlushManyAsync(context, messages: msgs);

        public static Task WriteAndFlushManyAsync(this IChannelHandlerContext context, ICollection<object> messages)
        {
            if (null == messages || messages.Count <= 0) { return TaskUtil.Completed; }

            var taskList = ThreadLocalList<Task>.NewInstance();
            foreach (object m in messages)
            {
                taskList.Add(context.WriteAsync(m));
            }
            context.Flush();

#if NET40
            var writeCloseCompletion = TaskEx.WhenAll(taskList);
            void returnAfterWriteAction(Task t) => taskList.Return();
            writeCloseCompletion.ContinueWith(returnAfterWriteAction, TaskContinuationOptions.ExecuteSynchronously);
            return writeCloseCompletion;
#else
            var writeCloseCompletion = Task.WhenAll(taskList);
            writeCloseCompletion.ContinueWith(ReturnAfterWriteAction, taskList, TaskContinuationOptions.ExecuteSynchronously);
            return writeCloseCompletion;
#endif
        }

        private static void ReturnAfterWriteAction(Task t, object s) => ((ThreadLocalList<Task>)s).Return();
    }
}
