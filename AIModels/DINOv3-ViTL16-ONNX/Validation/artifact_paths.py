"""Keep local validation output in the repository's ignored artifacts tree."""

from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]


def artifact_output(path):
    path = path.expanduser()
    output = (path if path.is_absolute() else REPOSITORY_ROOT / path).resolve()
    artifacts = (REPOSITORY_ROOT / "artifacts").resolve()
    if artifacts not in output.parents:
        raise ValueError("Output must be a subdirectory of the repository's artifacts/ directory.")
    return output
