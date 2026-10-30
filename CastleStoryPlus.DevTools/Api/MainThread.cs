using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CastleStoryPlus.DevTools.Api;

// Runs work from the API's threads on Unity's main thread (the game's objects may only be touched there), one
// frame at a time, and hands the result back to the waiting thread. At most BudgetMs of work a frame (at least one
// job): a burst of thousands of requests (a script scanning the route graph) froze the game for a second at once.
internal class MainThread : MonoBehaviour
{
	private sealed class Job
	{
		public Func<object> Work;

		public object Result;

		public Exception Error;

		public readonly ManualResetEvent Done = new ManualResetEvent(false);
	}

	private const double BudgetMs = 2.0;

	private static readonly Queue<Job> Jobs = new Queue<Job>();

	private static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();

	// Called from any thread; waits until the main thread has run the work (or the timeout ran out).
	internal static object Run(Func<object> work, int timeoutMs)
	{
		Job job = new Job { Work = work };
		lock (Jobs)
		{
			Jobs.Enqueue(job);
		}
		if (!job.Done.WaitOne(timeoutMs, false))
		{
			throw new TimeoutException("The game did not answer within " + timeoutMs + " ms (loading or frozen?)");
		}
		job.Done.Close();
		if (job.Error != null)
		{
			throw job.Error;
		}
		return job.Result;
	}

	private void Update()
	{
		Clock.Reset();
		Clock.Start();
		while (Clock.Elapsed.TotalMilliseconds < BudgetMs)
		{
			Job job;
			lock (Jobs)
			{
				if (Jobs.Count == 0)
				{
					return;
				}
				job = Jobs.Dequeue();
			}
			try
			{
				job.Result = job.Work();
			}
			catch (Exception ex)
			{
				job.Error = ex;
			}
			job.Done.Set();
		}
	}
}
