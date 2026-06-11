using System;
using System.Threading;

namespace Cysharp.Threading.Tasks.Internal
{
    internal sealed class ContinuationQueue
    {
        const int MAX_ARRAY_LENGTH = 0X7FEFFFFF;
        const int INITIAL_SIZE = 16;

        SpinLock _gate = new SpinLock(false);
        bool _isDequeuing = false;

        int _actionListCount = 0;
        Action[] _actionList = new Action[INITIAL_SIZE];

        int _waitingListCount = 0;
        Action[] _waitingList = new Action[INITIAL_SIZE];

        public ContinuationQueue()
        {
        }

        public void Enqueue(Action continuation)
        {
            bool lockTaken = false;
            try
            {
                _gate.Enter(ref lockTaken);

                if (_isDequeuing)
                {
                    if (_waitingList.Length == _waitingListCount)
                    {
                        int newLength = _waitingListCount * 2;
                        if ((uint)newLength > MAX_ARRAY_LENGTH)
                        {
                            newLength = MAX_ARRAY_LENGTH;
                        }

                        var newArray = new Action[newLength];
                        Array.Copy(_waitingList, newArray, _waitingListCount);
                        _waitingList = newArray;
                    }
                    _waitingList[_waitingListCount] = continuation;
                    _waitingListCount++;
                }
                else
                {
                    if (_actionList.Length == _actionListCount)
                    {
                        int newLength = _actionListCount * 2;
                        if ((uint)newLength > MAX_ARRAY_LENGTH)
                        {
                            newLength = MAX_ARRAY_LENGTH;
                        }

                        var newArray = new Action[newLength];
                        Array.Copy(_actionList, newArray, _actionListCount);
                        _actionList = newArray;
                    }
                    _actionList[_actionListCount] = continuation;
                    _actionListCount++;
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _gate.Exit(false);
                }
            }
        }

        public int Clear()
        {
            int rest = _actionListCount + _waitingListCount;

            _actionListCount = 0;
            _actionList = new Action[INITIAL_SIZE];

            _waitingListCount = 0;
            _waitingList = new Action[INITIAL_SIZE];

            return rest;
        }

        public void Run()
        {
            {
                bool lockTaken = false;
                try
                {
                    _gate.Enter(ref lockTaken);
                    if (_actionListCount == 0)
                    {
                        return;
                    }

                    _isDequeuing = true;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _gate.Exit(false);
                    }
                }
            }

            for (int i = 0; i < _actionListCount; i++)
            {
                Action action = _actionList[i];
                _actionList[i] = null;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    UniTaskScheduler.PublishUnobservedTaskException(ex);
                }
            }

            {
                bool lockTaken = false;
                try
                {
                    _gate.Enter(ref lockTaken);
                    _isDequeuing = false;

                    var swapTempActionList = _actionList;

                    _actionListCount = _waitingListCount;
                    _actionList = _waitingList;

                    _waitingListCount = 0;
                    _waitingList = swapTempActionList;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _gate.Exit(false);
                    }
                }
            }
        }
    }
}
