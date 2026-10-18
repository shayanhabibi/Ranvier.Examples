# Following an order through Ranvier

Start with either console entry point, then open the model in the same language. The model owns state and pricing; the host owns its graph and the way a person supplies inputs.

## State and derivation

Quantity, unit price, discount percentage, delivery choice and address are independent signals. The F# implementation uses `createSignal` and `createMemo`; the C# implementation uses `Reactive.Signal` and `Reactive.Memo`.

Subtotal, rounded discount, delivery charge and validation are memos. The payable-total memo returns `None` in F# or `null` in C# while validation fails. A separate eligibility memo drives submission. Validation errors are values with field names, making them usable in a terminal or a form.

An effect or binding reads these results. When an input changes, Ranvier tracks which derived values must update and notifies consumers. Neither host calls a price-recalculation routine.

Numeric derivations check their accepted ranges before multiplying. An invalid quantity or price gives a zero subtotal, and an invalid discount gives a zero discount amount; the payable total remains unavailable. This keeps the live breakdown readable even when a caller supplies extreme decimal values.

## Conditional address validation

The address-error memo reads delivery first. Pickup returns a successful address result immediately. Delivery reads the address and requires nonblank text.

Changing a pickup address therefore leaves address validation unobserved until delivery is selected. The tests attach a consumer to this memo and check the dependency change directly.

## A coherent reset

Reset changes all five inputs and clears the confirmation inside a batch. A summary consumer sees the final combination once. The test starts from a modified delivery order, attaches an observer, resets, and checks that only one additional summary arrives.

Batching groups notifications; it is not a transactional database operation and does not roll back applied writes.

## Submission snapshots

Submit reads the validated payable total and captures an immutable confirmation record. Later edits change the live order, while the last accepted record remains fixed. Invalid submissions are rejected inside the model even if a caller bypasses the disabled UI button.

## F# controls and effects

`OrderWindow.fs` creates Avalonia controls and subscribes to their events. These events update the model's signals inside the graph context. An effect copies current pricing, field errors and command eligibility into the controls. A painting guard prevents programmatic refreshes from feeding back into edits.

Numeric fields accept decimal editor values; quantity additionally requires an integer. Empty or malformed text invalidates the corresponding model input, making the total unavailable. Field errors explain the accepted range. Reset restores every input.

Subscriptions are disposable handles. Closing the window disposes the event handlers, the model and its graph.

## C# properties and commands

`OrderViewModel` derives from `ReactiveObject`. `Bindings.Writable` wraps the model's existing signals; `Bindings.Computed` turns model reads into observable XAML properties. The bindings raise notifications for derived values.

Reset and Submit are `ReactiveCommand` values. Submit's predicate reads the model's eligibility memo. Avalonia observes command eligibility and updates the button's effective enabled state.

The nullable numeric adapter maps empty values to invalid model inputs; the window also rejects malformed editor text. The XAML binds to the view model; pricing rules stay in the model.

Disposing the view model releases its binding scope. The host then disposes the separately owned model and graph.

## Graph lifetime

Every host creates a graph and keeps it alive until the application ends. A short activation or `graph.Run` selects it during setup or a mutation. The model constructs a nested ownership scope on that graph.

The model does not dispose the supplied graph. Tests create an unrelated signal, dispose the model, and show that the signal remains usable. Summary observers have their own disposable scopes and also stop when the model is disposed.

## Verifying the examples

The F# Expecto suite and C# xUnit suite cover the same field ranges, money examples, conditional address reads, batched reset, snapshots and disposal. The C# suite also checks view-model notifications and both desktop implementations.

The headless tests create real Avalonia windows, supply edits, send keyboard input to buttons, inspect controls, render frames and close the windows. This checks UI behavior without launching visible windows. Manual native-platform checks remain useful for focus, accessibility, display scaling and window-manager integration.
