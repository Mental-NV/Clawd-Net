#!/usr/bin/env python3
"""
Backlog semantic validator.

Validates docs/backlog.json against:
1. JSON Schema (backlog.schema.json)
2. Semantic rules that JSON Schema cannot express

Exit codes:
  0 - validation passed
  1 - validation failed
"""

import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

try:
    import jsonschema
except ImportError:
    print("Error: jsonschema library not found. Install with: pip install jsonschema", file=sys.stderr)
    sys.exit(1)


def load_json(path: Path) -> Any:
    """Load and parse JSON file."""
    try:
        with open(path, 'r', encoding='utf-8') as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"Error: File not found: {path}", file=sys.stderr)
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON in {path}: {e}", file=sys.stderr)
        sys.exit(1)


def validate_schema(backlog: Dict[str, Any], schema: Dict[str, Any]) -> List[str]:
    """Validate backlog against JSON Schema. Returns list of errors."""
    errors = []
    try:
        jsonschema.validate(instance=backlog, schema=schema)
    except jsonschema.ValidationError as e:
        errors.append(f"Schema validation failed: {e.message}")
        if e.path:
            path_str = ".".join(str(p) for p in e.path)
            errors.append(f"  at path: {path_str}")
    except jsonschema.SchemaError as e:
        errors.append(f"Invalid schema: {e.message}")
    return errors


def validate_unique_ids(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that all item IDs are unique."""
    errors = []
    ids = [item['id'] for item in items]
    seen: Set[str] = set()
    duplicates: Set[str] = set()

    for item_id in ids:
        if item_id in seen:
            duplicates.add(item_id)
        seen.add(item_id)

    if duplicates:
        errors.append(f"Duplicate item IDs found: {', '.join(sorted(duplicates))}")

    return errors


def validate_unique_orders(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that all order values are unique."""
    errors = []
    orders = [(item['order'], item['id']) for item in items]
    seen: Dict[int, str] = {}

    for order, item_id in orders:
        if order in seen:
            errors.append(f"Duplicate order value {order} used by items: {seen[order]}, {item_id}")
        else:
            seen[order] = item_id

    return errors


def validate_dependencies(items: List[Dict[str, Any]]) -> List[str]:
    """Validate dependency references and check for cycles."""
    errors = []
    item_ids = {item['id'] for item in items}

    # Check that all dependency references exist
    for item in items:
        depends_on = item.get('dependsOn', [])
        for dep_id in depends_on:
            if dep_id not in item_ids:
                errors.append(f"Item '{item['id']}' depends on non-existent item '{dep_id}'")

    # Check for cycles using DFS
    def has_cycle(item_id: str, visited: Set[str], rec_stack: Set[str]) -> Tuple[bool, List[str]]:
        """DFS to detect cycles. Returns (has_cycle, cycle_path)."""
        visited.add(item_id)
        rec_stack.add(item_id)

        item = next((i for i in items if i['id'] == item_id), None)
        if item:
            for dep_id in item.get('dependsOn', []):
                if dep_id not in visited:
                    has_cycle_result, path = has_cycle(dep_id, visited, rec_stack)
                    if has_cycle_result:
                        return True, [item_id] + path
                elif dep_id in rec_stack:
                    return True, [item_id, dep_id]

        rec_stack.remove(item_id)
        return False, []

    visited: Set[str] = set()
    for item in items:
        if item['id'] not in visited:
            has_cycle_result, cycle_path = has_cycle(item['id'], visited, set())
            if has_cycle_result:
                cycle_str = " -> ".join(cycle_path)
                errors.append(f"Dependency cycle detected: {cycle_str}")
                break  # Report first cycle only

    return errors


def validate_active_items(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that at most one item is active (in_progress or ready_for_validation)."""
    errors = []
    active_statuses = {'in_progress', 'ready_for_validation'}
    active_items = [item for item in items if item['status'] in active_statuses]

    if len(active_items) > 1:
        active_ids = [item['id'] for item in active_items]
        errors.append(f"Multiple active items found (expected at most 1): {', '.join(active_ids)}")

    return errors


def validate_dependency_readiness(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that active items have all dependencies completed."""
    errors = []
    active_statuses = {'in_progress', 'ready_for_validation'}
    item_map = {item['id']: item for item in items}

    for item in items:
        if item['status'] in active_statuses:
            depends_on = item.get('dependsOn', [])
            for dep_id in depends_on:
                dep_item = item_map.get(dep_id)
                if dep_item and dep_item['status'] != 'done':
                    errors.append(
                        f"Item '{item['id']}' is {item['status']} but depends on "
                        f"'{dep_id}' which is {dep_item['status']} (expected done)"
                    )

    return errors


def validate_done_items(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that done items have all deliverables and exit criteria marked done."""
    errors = []

    for item in items:
        if item['status'] == 'done':
            # Check deliverables
            deliverables = item.get('deliverables', [])
            incomplete_deliverables = [d['id'] for d in deliverables if not d.get('done', False)]
            if incomplete_deliverables:
                errors.append(
                    f"Item '{item['id']}' is done but has incomplete deliverables: "
                    f"{', '.join(incomplete_deliverables)}"
                )

            # Check exit criteria
            exit_criteria = item.get('exitCriteria', [])
            incomplete_criteria = [c['id'] for c in exit_criteria if not c.get('done', False)]
            if incomplete_criteria:
                errors.append(
                    f"Item '{item['id']}' is done but has incomplete exit criteria: "
                    f"{', '.join(incomplete_criteria)}"
                )

    return errors


def validate_blocked_items(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that blocked items include blockedReason."""
    errors = []

    for item in items:
        if item['status'] == 'blocked':
            if not item.get('blockedReason'):
                errors.append(f"Item '{item['id']}' is blocked but missing blockedReason")

    return errors


def validate_state_transitions(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that status values follow allowed transitions."""
    errors = []
    allowed_statuses = {'todo', 'in_progress', 'ready_for_validation', 'done', 'blocked', 'deferred', 'cancelled'}

    for item in items:
        status = item['status']
        if status not in allowed_statuses:
            errors.append(f"Item '{item['id']}' has invalid status: {status}")

    return errors


def validate_checklist_item_ids(items: List[Dict[str, Any]]) -> List[str]:
    """Validate that checklist item IDs are unique within each parent item."""
    errors = []

    for item in items:
        # Check deliverables
        deliverable_ids = [d['id'] for d in item.get('deliverables', [])]
        seen: Set[str] = set()
        for did in deliverable_ids:
            if did in seen:
                errors.append(f"Item '{item['id']}' has duplicate deliverable ID: {did}")
            seen.add(did)

        # Check exit criteria
        criteria_ids = [c['id'] for c in item.get('exitCriteria', [])]
        seen = set()
        for cid in criteria_ids:
            if cid in seen:
                errors.append(f"Item '{item['id']}' has duplicate exit criteria ID: {cid}")
            seen.add(cid)

    return errors


def main():
    """Main validation entry point."""
    repo_root = Path(__file__).parent.parent
    backlog_path = repo_root / "docs" / "backlog.json"
    schema_path = repo_root / "docs" / "backlog.schema.json"

    # Load files
    backlog = load_json(backlog_path)
    schema = load_json(schema_path)

    # Collect all errors
    all_errors: List[str] = []

    # Schema validation
    all_errors.extend(validate_schema(backlog, schema))

    # Semantic validation (only if schema validation passed)
    if not all_errors:
        items = backlog.get('items', [])

        all_errors.extend(validate_unique_ids(items))
        all_errors.extend(validate_unique_orders(items))
        all_errors.extend(validate_dependencies(items))
        all_errors.extend(validate_active_items(items))
        all_errors.extend(validate_dependency_readiness(items))
        all_errors.extend(validate_done_items(items))
        all_errors.extend(validate_blocked_items(items))
        all_errors.extend(validate_state_transitions(items))
        all_errors.extend(validate_checklist_item_ids(items))

    # Report results
    if all_errors:
        print("Validation failed:", file=sys.stderr)
        for error in all_errors:
            print(f"  - {error}", file=sys.stderr)
        sys.exit(1)
    else:
        print("Validation passed: backlog.json is valid")
        sys.exit(0)


if __name__ == '__main__':
    main()
