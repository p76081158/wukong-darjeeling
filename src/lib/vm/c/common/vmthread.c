/*
 * vmthread.c
 * 
 * Copyright (c) 2008-2010 CSIRO, Delft University of Technology.
 * 
 * This file is part of Darjeeling.
 * 
 * Darjeeling is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * Darjeeling is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with Darjeeling.  If not, see <http://www.gnu.org/licenses/>.
 */
 
#include <string.h>

#include "types.h"
#include "vmthread.h"
#include "heap.h"
#include "vm.h"
#include "execution.h"
#include "djtimer.h"
#include "debug.h"
#include "global_id.h"

//platform-specific
#include "config.h"

// generated by the infuser
#include "jlib_base.h"


/**
 * Creates a new dj_thread object.
 * @return a new dj_tread object, or null if fail (out of memory)
 */
dj_thread *dj_thread_create()
{
	dj_thread *ret = (dj_thread*)dj_mem_alloc(sizeof(dj_thread), CHUNKID_THREAD);

	// if we're out of memory, let the caller handle it
	if (ret==NULL)
    {
        DEBUG_LOG(DBG_DARJEELING, "can't create thread:null\n");
        return NULL;
    }

	// init thread properties
	ret->frameStack = NULL;
	ret->status = THREADSTATUS_CREATED;
	ret->scheduleTime = 0;
	ret->id = 0;
	ret->next = NULL;
	ret->priority = 0;
	ret->runnable = NULL;
	ret->monitorObject = NULL;

	return ret;
}

void dj_thread_destroy(dj_thread *thread)
{
	dj_mem_free(thread);
}

/**
 * Pushes a new stack frame onto the stack. Used by the execution engine to enter methods.
 * @param thread the thread to push the frame onto
 * @param frame the frame to push
 */
void dj_thread_pushFrame(dj_frame *frame)
{
	dj_thread *thread = dj_exec_getCurrentThread();
	frame->parent = thread->frameStack;
	thread->frameStack = frame;
}

/**
 * Pops a stack frame from the frame stack. Used by the execution engine to leave methods and throw
 * exceptions.
 * @param thread the thread to pop a stack frame from
 * @return the popped frame
 */
dj_frame *dj_thread_popFrame()
{
	dj_thread *thread = dj_exec_getCurrentThread();
	dj_frame *ret = thread->frameStack;
	thread->frameStack = thread->frameStack->parent;
	return ret;
}

void dj_frame_markRootSet(dj_frame *frame)
{
	int i;
	ref_t *stack, *locals;

#ifndef EXECUTION_FRAME_ON_STACK
	// Mark the frame object as BLACK (don't collect, don't inspect further)
	dj_mem_setChunkColor(frame, TCM_BLACK);
#endif

	// Mark every object on the reference stack
	stack = dj_frame_getReferenceStackBase(frame, dj_global_id_getMethodImplementation(frame->method));
	uint16_t nr_ref_stack = dj_exec_getNumberOfObjectsOnReferenceStack(frame);
	for (i=0; i<nr_ref_stack; i++)
		dj_mem_setRefGrayIfWhite(stack[i]);

	// Mark every object in the local variables
	locals = dj_frame_getLocalReferenceVariables(frame, dj_global_id_getMethodImplementation(frame->method));
	for (i=0; i<dj_di_methodImplementation_getReferenceLocalVariableCount(dj_global_id_getMethodImplementation(frame->method)); i++)
		dj_mem_setRefGrayIfWhite(locals[i]);

}

void dj_frame_updatePointers(dj_frame * frame)
{
	int i;
	ref_t *stack, *locals;

	dj_di_pointer methodImpl = dj_global_id_getMethodImplementation(frame->method);

	// Update references on the reference stack
	stack = dj_frame_getReferenceStackBase(frame, methodImpl);
	uint16_t nr_ref_stack = dj_exec_getNumberOfObjectsOnReferenceStack(frame);
	for (i=0; i<nr_ref_stack; i++)
		stack[i] = dj_mem_getUpdatedReference(stack[i]);

#ifndef EXECUTION_FRAME_ON_STACK
	// Update the saved refStack and intStack pointers
	// (note that intStack may point to the real stack or the reference stack, depending on if this method is rtc compiled or not)
	// DEBUG_LOG(DBG_DARJEELING, "dj_frame_updatePointers before frame %p ref %p int %p shift %d\n", frame, frame->saved_refStack, frame->saved_intStack, frame_shift);
	uint16_t frame_shift = dj_mem_getChunkShift(frame);
	frame->saved_refStack = (ref_t *)(((void *)frame->saved_refStack) - frame_shift);
	if (dj_mem_isHeapPointer(frame->saved_intStack)) {
		// the intStack is in the frame, so update the pointer. (DON'T UPDATE THE POINTER IF IT POINTS TO THE ACTUAL STACK!)
		frame->saved_intStack = (int16_t *)(((void *)frame->saved_intStack) - frame_shift);
	}
	// DEBUG_LOG(DBG_DARJEELING, "dj_frame_updatePointers after  frame %p ref %p int %p\n", frame, frame->saved_refStack, frame->saved_intStack);
	frame->parent = dj_mem_getUpdatedPointer(frame->parent);
#endif

	// Update the local variables
	locals = dj_frame_getLocalReferenceVariables(frame, methodImpl);
	for (i=0; i<dj_di_methodImplementation_getReferenceLocalVariableCount(methodImpl); i++)
		locals[i] = dj_mem_getUpdatedReference(locals[i]);

	// update pointers to the infusion and parent frame
	// NOTE these have to be updated AFTER the stack and local variable frame
	frame->method.infusion = dj_mem_getUpdatedPointer(frame->method.infusion);
}

void dj_thread_markRootSet(dj_thread *thread)
{
	dj_frame *frame;

	// mark the thread object as BLACK (don't collect, don't inspect further)
	// finished threads may be reclaimed by the GC
	if (thread->status!=THREADSTATUS_FINISHED)
		dj_mem_setChunkColor(thread, TCM_BLACK);

	// mark the thread's monitor object and name string as GRAY
	if (thread->monitorObject!=NULL) dj_mem_setRefGrayIfWhite(VOIDP_TO_REF(thread->monitorObject));
	if (thread->runnable!=NULL) dj_mem_setRefGrayIfWhite(VOIDP_TO_REF(thread->runnable));

	// mark each of the frames
	frame = thread->frameStack;
	while (frame!=NULL)
	{
		dj_frame_markRootSet(frame);
		frame = frame->parent;
	}
}

void dj_thread_updatePointers(dj_thread * thread)
{
	// mark each of the frames
	dj_frame *frame = thread->frameStack;
	dj_frame *next_frame;
	while (frame!=NULL)
	{
		next_frame = frame->parent; // save this since it might change after updating the pointers (only if the frames are on the heap)
		dj_frame_updatePointers(frame);
		frame = next_frame;
	}

#ifndef EXECUTION_FRAME_ON_STACK
	thread->frameStack = dj_mem_getUpdatedPointer(thread->frameStack);
#endif
	thread->monitorObject = dj_mem_getUpdatedPointer(thread->monitorObject);
	thread->next = dj_mem_getUpdatedPointer(thread->next);
	thread->runnable = dj_mem_getUpdatedPointer(thread->runnable);

}


/**
 * Puts a thread to sleep. Sets the state of the thread to THREADSTATUS_SLEEPING and sets its timer to
 * the given timeout.
 * @param thread the thread to sleep
 * @param time the number of milliseconds to sleep
 */
void dj_thread_sleep(dj_thread *thread, dj_time_t time)
{
	dj_time_t sleepTime = dj_timer_getTimeMillis() + time;
	thread->status = THREADSTATUS_SLEEPING;
	thread->scheduleTime = sleepTime;
}

void dj_thread_wait(dj_thread * thread, dj_object * object, dj_time_t time)
{
	thread->status = THREADSTATUS_WAITING_FOR_MONITOR;
	thread->scheduleTime = time==0?0:(dj_timer_getTimeMillis() + time);
	thread->monitorObject = object;
}


#ifndef EXECUTION_FRAME_ON_STACK
/**
 * Creates a new dj_frame object for a given method implementation.
 * @param methodImplId the method implementation this frame will be executing
 * @return a newly created dj_frame object, or null if fail (out of memory)
 */
dj_frame *dj_frame_create_fast(dj_global_id methodImplId, dj_di_pointer methodImpl)
{
	// Mark 90 at 0 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 91 at 43 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 92 at 26 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 93 at 9 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 94 at 107 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 95 at 4 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 96 at 39 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 97 at 29 cycles since last mark. (already deducted 5 cycles for timer overhead)
	// Mark 98 at 1 cycles since last mark. (already deducted 5 cycles for timer overhead)

	// UNNECESSARY SINCE WE ASSUME THE INFUSION WON'T MOVE (we will have a lot more problems in AOT code if it does)
	// dj_infusion * infusion = methodImplId.infusion;
// avroraCallMethodTimerMark(89);
	// UNNECESSARY SINCE WE ASSUME THE INFUSION WON'T MOVE (we will have a lot more problems in AOT code if it does)
	// dj_mem_addSafePointer((void**)&infusion);
// avroraCallMethodTimerMark(90);
	// dj_di_pointer methodImpl = dj_global_id_getMethodImplementation(methodImplId);

// avroraCallMethodTimerMark(91);
	// calculate the size of the frame to create
	// int localVariablesSize =
	// 	(dj_di_methodImplementation_getReferenceLocalVariableCount(methodImpl) * sizeof(ref_t)) +
	// 	(dj_di_methodImplementation_getIntegerLocalVariableCount(methodImpl) * sizeof(int16_t));

// avroraCallMethodTimerMark(92);
	// int size =
	// 	sizeof(dj_frame) +
	// 	(dj_di_methodImplementation_getMaxStack(methodImpl) * sizeof(int16_t)) +
	// 	localVariablesSize
	// 	;
	// Note that integer variables 'grow' down in the stack frame, so dj_di_methodImplementation_getOffsetToLocalIntegerVariables is also the size of the frame, -2 because the address of the 'first' int variable is 2 lower than the size of the frame (since slots are 16-bit).
	int size = sizeof(dj_frame)
				+ dj_di_methodImplementation_getOffsetToLocalIntegerVariables(methodImpl) + 2;

// avroraCallMethodTimerMark(93);

	dj_frame *ret = (dj_frame*)dj_mem_alloc(size, CHUNKID_FRAME);
// avroraCallMethodTimerMark(94);

	// in case of null, return and let the caller deal with it
	if (ret==NULL)
    {
        DEBUG_LOG(DBG_DARJEELING, "could not create frame:null\n");
    } else
    {
    	// restore a potentially invalid infusion pointer
// avroraCallMethodTimerMark(95);
    	// UNNECESSARY SINCE WE ASSUME THE INFUSION WON'T MOVE (we will have a lot more problems in AOT code if it does)
    	// methodImplId.infusion = infusion;

		// init the frame
		ret->method = methodImplId;
		ret->parent = NULL;
#ifndef EXECUTION_DISABLEINTERPRETER_COMPLETELY
		ret->pc = 0;
		ret->saved_intStack = dj_frame_getIntegerStackBase(ret, methodImpl);
#endif
		ret->saved_refStack = dj_frame_getReferenceStackBase(ret, methodImpl);

// avroraCallMethodTimerMark(96);
		// set local variables to 0/null
		// memset(dj_frame_getLocalReferenceVariables(ret, methodImpl), 0, localVariablesSize);
		void * start = ((void*)ret) + sizeof(dj_frame) + dj_di_methodImplementation_getMaxStack(methodImpl) * sizeof(int16_t);
		void * end = ((void*)ret) + size;
		memset(start, 0, end-start);

// avroraCallMethodTimerMark(97);
    }

	// UNNECESSARY SINCE WE ASSUME THE INFUSION WON'T MOVE (we will have a lot more problems in AOT code if it does)
	// dj_mem_removeSafePointer((void**)&infusion);
// avroraCallMethodTimerMark(98);

	return ret;
}
#endif // ifndef EXECUTION_FRAME_ON_STACK

/**
 * Creates a new monitor block.
 * @return a newly created monitor block, or null if failed (out of memory)
 */
dj_monitor_block * dj_monitor_block_create()
{
	dj_monitor_block * ret = (dj_monitor_block *)dj_mem_alloc(sizeof(dj_monitor_block), CHUNKID_MONITOR_BLOCK);
	if (ret==NULL) return NULL;

    memset(ret, 0, sizeof(dj_monitor_block));

	return ret;
}

void dj_monitor_block_updatePointers(dj_monitor_block * monitor_block)
{
	int i;
	monitor_block->next = dj_mem_getUpdatedPointer(monitor_block->next);
	for (i=0; i<monitor_block->count; i++)
	{
		monitor_block->monitors[i].object = dj_mem_getUpdatedPointer(monitor_block->monitors[i].object);
		monitor_block->monitors[i].owner = dj_mem_getUpdatedPointer(monitor_block->monitors[i].owner);
	}
}

void dj_monitor_markRootSet(dj_monitor_block * monitor_block)
{
	int i;

	// Mark the monitor object as BLACK (don't collect, don't inspect further)
	dj_mem_setChunkColor(monitor_block, TCM_BLACK);

	for (i=0; i<monitor_block->count; i++)
		dj_mem_setRefGrayIfWhite(VOIDP_TO_REF(monitor_block->monitors[i].object));

}

