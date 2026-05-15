================================================================================
                    GIT-CLONE PROJECT - TECHNICAL DOCUMENTATION
================================================================================

A complete version control system implementation in C# demonstrating cryptographic
hashing, Merkle trees, and content-addressable storage.

================================================================================
TABLE OF CONTENTS
================================================================================

1. Mathematical Foundation
   1.1 SHA-256 Cryptographic Hashing
   1.2 Merkle Trees
   1.3 Merkle DAG (Directed Acyclic Graph)
   1.4 Content-Addressable Storage

2. How the Program Works
   2.1 Architecture Overview
   2.2 Core Components
   2.3 Authentication & Authorization
   2.4 Command Reference

3. .gitclone Folder Structure
   3.1 Directory Layout
   3.2 Object Storage Format
   3.3 Reference Storage
   3.4 Authentication Storage

4. Mathematical Examples
   5.1 Hash Computation Example
   5.2 Tree Structure Example
   5.3 Commit Chain Example

5. Security Features
   6.1 Password Hashing (PBKDF2)
   6.2 Session Management
   6.3 Role-Based Access Control


================================================================================
1. MATHEMATICAL FOUNDATION
================================================================================

┌─────────────────────────────────────────────────────────────────────────────┐
│ 1.1 SHA-256 CRYPTOGRAPHIC HASHING                                          │
└─────────────────────────────────────────────────────────────────────────────┘

The entire system is built on SHA-256 (Secure Hash Algorithm 256-bit), a 
cryptographic hash function that produces a 64-character hexadecimal output.

PROPERTIES OF SHA-256 USED:

┌─────────────────────────────────────────────────────────────────────────────┐
│ Property          │ Description                                            │
├───────────────────┼─────────────────────────────────────────────────────────┤
│ Deterministic     │ Same input → Same hash every time                      │
│ Fast Computation  │ Can hash large files quickly                           │
│ Pre-image Resistant│ Cannot reverse hash to find original input            │
│ Collision Resistant│ Extremely unlikely two inputs produce same hash       │
│ Avalanche Effect  │ Small change → Completely different hash               │
└─────────────────────────────────────────────────────────────────────────────┘

HASH COMPUTATION PROCESS:

    Input Data (bytes) 
           │
           ▼
    ┌──────────────┐
    │  SHA-256     │  ← 64 rounds of compression
    │  Algorithm   │
    └──────────────┘
           │
           ▼
    64-character Hexadecimal String
    Example: "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3"

HASH SIZES:

    ┌────────────────────────────────────────────────────┐
    │ Raw hash bytes: 32 bytes (256 bits)               │
    │ Hex string: 64 characters                          │
    │ Short hash (display): 8 characters                 │
    └────────────────────────────────────────────────────┘

WHY HASHING IS CRITICAL FOR VERSION CONTROL:

    1. CONTENT ADDRESSING: Files are identified by their content hash
    2. INTEGRITY VERIFICATION: Any corruption changes the hash
    3. DEDUPLICATION: Same content → Same hash → Stored once
    4. TAMPER EVIDENCE: Changing any byte changes all parent hashes


┌─────────────────────────────────────────────────────────────────────────────┐
│ 1.2 MERKLE TREES                                                            │
└─────────────────────────────────────────────────────────────────────────────┘

A Merkle tree is a hierarchical data structure where every node is a hash of 
its children. Git uses Merkle trees to represent directory structures.

MATHEMATICAL DEFINITION:

    For a tree node N with children C₁, C₂, ..., Cₙ:
    
    Hash(N) = SHA-256( Hash(C₁) || Hash(C₂) || ... || Hash(Cₙ) )
    
    Where || denotes concatenation of bytes.

TREE STRUCTURE EXAMPLE:

                          ┌─────────────────┐
                          │   ROOT TREE     │
                          │   Hash: ABC     │
                          └────────┬────────┘
                                   │
              ┌────────────────────┼────────────────────┐
              │                    │                    │
              ▼                    ▼                    ▼
    ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
    │   src/ (tree)   │  │ README.md (blob)│  │ config (blob)   │
    │   Hash: DEF     │  │   Hash: GHI     │  │   Hash: JKL     │
    └────────┬────────┘  └─────────────────┘  └─────────────────┘
             │
      ┌──────┼──────┐
      │      │      │
      ▼      ▼      ▼
    file1  file2  subdir/
    (blob) (blob) (tree)

HOW MERKLE TREES ENABLE EFFICIENT COMPARISON:

    To detect changes between two commits:
    
    ┌─────────────────────────────────────────────────────────────┐
    │ Compare root tree hashes                                    │
    │   ├─ If equal → No changes (fast)                           │
    │   └─ If different → Compare children recursively           │
    │         ├─ Only traverse different branches                 │
    │         └─ O(log n) time complexity                        │
    └─────────────────────────────────────────────────────────────┘

EXAMPLE OF CHANGE PROPAGATION:

    When file1.txt changes:
    
    file1.txt content ──► New blob hash (different)
           │
           ▼
    src/tree hash changes (includes new blob hash)
           │
           ▼
    Root tree hash changes (includes new src hash)
           │
           ▼
    Commit hash changes (includes new root tree hash)

RESULT: A single byte change changes the root commit hash completely!


┌─────────────────────────────────────────────────────────────────────────────┐
│ 1.3 MERKLE DAG (DIRECTED ACYCLIC GRAPH)                                     │
└─────────────────────────────────────────────────────────────────────────────┘

Git commits form a Merkle DAG where each commit points to:
    1. A tree (snapshot of files)
    2. Parent commit(s)

STRUCTURE:

    Commit A ──────► Tree A (files at time A)
       │
       ▼
    Commit B ──────► Tree B
       │
       ▼
    Commit C ──────► Tree C

BRANCHING CREATES A DAG:

    main:  A ← B ← C ← D
                      │
    feature:          └── E ← F

MATHEMATICAL PROPERTIES:

    ┌─────────────────────────────────────────────────────────────┐
    │ Each commit has a unique hash:                              │
    │ Hash(Commit) = SHA-256(TreeHash + ParentHash + Metadata)   │
    │                                                             │
    │ This creates an immutable history where:                    │
    │ • Changing any commit changes all descendants               │
    │ • History cannot be rewritten without detection            │
    │ • Branching creates O(1) new commits                       │
    └─────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│ 1.4 CONTENT-ADDRESSABLE STORAGE                                            │
└─────────────────────────────────────────────────────────────────────────────┘

Objects are stored using their hash as the address/filename.

STORAGE PATH ALGORITHM:

    Given hash: "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3"
    
    Step 1: Take first 2 characters as directory → "a9"
    Step 2: Remaining 62 characters as filename → "4a8fe5ccb19ba61c4c0873d391e987982fbbd3"
    Step 3: Store at: objects/a9/4a8fe5ccb19ba61c4c0873d391e987982fbbd3

WHY THIS SHARDING:

    ┌─────────────────────────────────────────────────────────────┐
    │ • 256 possible directories (00-FF)                          │
    │ • Even distribution of objects                              │
    │ • Prevents directory with too many files                    │
    │ • Efficient lookup: O(1) to find object                    │
    └─────────────────────────────────────────────────────────────┘


================================================================================
2. HOW THE PROGRAM WORKS
================================================================================

┌─────────────────────────────────────────────────────────────────────────────┐
│ 2.1 ARCHITECTURE OVERVIEW                                                  │
└─────────────────────────────────────────────────────────────────────────────┘

    ┌─────────────────────────────────────────────────────────────────────────┐
    │                         USER INTERFACE (CLI)                            │
    │                              Program.cs                                  │
    └─────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
    ┌─────────────────────────────────────────────────────────────────────────┐
    │                         GIT REPOSITORY                                  │
    │                          GitRepository.cs                               │
    │  • Commit operations    • Branch management    • Authorization         │
    │  • Revert operations    • Checkout            • Session management     │
    └─────────────────────────────────────────────────────────────────────────┘
                                      │
              ┌───────────────────────┼───────────────────────┐
              │                       │                       │
              ▼                       ▼                       ▼
    ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
    │   OBJECT STORE  │     │  REF MANAGER    │     │  AUTH SERVICE   │
    │  ObjectStore.cs │     │ReferenceManager │     │  AuthService.cs │
    │                 │     │                 │     │                 │
    │ • Store objects │     │ • Branches      │     │ • Users         │
    │ • Retrieve by   │     │ • HEAD pointer  │     │ • Roles         │
    │   hash          │     │ • References    │     │ • Permissions   │
    └─────────────────┘     └─────────────────┘     └─────────────────┘
              │
              ▼
    ┌─────────────────────────────────────────────────────────────────────────┐
    │                          FILE SYSTEM                                    │
    │                         .gitclone/ folder                               │
    └─────────────────────────────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│ 2.2 CORE COMPONENTS                                                         │
└─────────────────────────────────────────────────────────────────────────────┘

OBJECT TYPES:

┌────────────┬────────────────────────────────────────────────────────────────┐
│ BLOB       │ Stores raw file content                                       │
│            │ Hash = SHA-256(file content)                                  │
│            │ Used for: All tracked files                                   │
├────────────┼────────────────────────────────────────────────────────────────┤
│ TREE       │ Stores directory structure                                    │
│            │ Hash = SHA-256(concatenated entry hashes)                     │
│            │ Used for: Directories and subdirectories                      │
├────────────┼────────────────────────────────────────────────────────────────┤
│ COMMIT     │ Stores snapshot metadata                                      │
│            │ Hash = SHA-256(tree hash + parent hash + author + timestamp)  │
│            │ Used for: Version history points                              │
└────────────┴────────────────────────────────────────────────────────────────┘

COMMIT WORKFLOW:

    1. User runs: commit "message"
    
    2. System creates BLOBs for all files:
       For each file → Read bytes → SHA-256 → Store as blob
    
    3. System creates TREE structure:
       For each directory → Collect entry hashes → SHA-256 → Store as tree
    
    4. System creates COMMIT:
       tree_hash = root tree hash
       parent_hash = previous commit (or null)
       author = current user
       timestamp = UTC now
       message = user input
       
       commit_hash = SHA-256(all above)
    
    5. Update branch reference:
       .gitclone/refs/heads/main ← new commit hash

REVERT WORKFLOW (Mathematical):

    To revert commit C that changed files from state P (parent) to state C:
    
    Let:
        P = Tree before commit C
        C = Tree after commit C  
        H = Current HEAD tree
    
    For each file f:
        if f in C but not in P:  (file was ADDED)
            remove f from H
    
        elif f in P but not in C: (file was DELETED)  
            restore f from P to H
    
        elif hash_P(f) ≠ hash_C(f): (file was MODIFIED)
            restore f from P to H
    
    New commit = SHA-256(H + metadata)


┌─────────────────────────────────────────────────────────────────────────────┐
│ 2.3 AUTHENTICATION & AUTHORIZATION                                          │
└─────────────────────────────────────────────────────────────────────────────┘

ROLE HIERARCHY:

    Level 0: Guest (👁️)  - Read-only access
    Level 1: User (👤)    - Can commit and view
    Level 2: Developer(🔧) - Can create branches and revert
    Level 3: Maintainer(⭐)- Can delete branches
    Level 4: Admin (👑)   - Full system access

PERMISSION SYSTEM (Bit Flags):

    Permission.Read         = 1 << 0  = 1
    Permission.Write        = 1 << 1  = 2
    Permission.Commit       = 1 << 2  = 4
    Permission.CreateBranch = 1 << 3  = 8
    Permission.DeleteBranch = 1 << 4  = 16
    Permission.Merge        = 1 << 5  = 32
    Permission.ManageUsers  = 1 << 8  = 256

    Role permissions are bitwise OR of individual permissions:
    
    User permissions = Read | Write | Commit = 1 + 2 + 4 = 7
    Developer = User | CreateBranch | Merge = 7 + 8 + 32 = 47

PASSWORD HASHING (PBKDF2):

    Not plain SHA-256! Uses key stretching:
    
    Password + Salt ──► 310,000 iterations of HMAC-SHA256 ──► Final Hash
    
    This makes brute-force attacks computationally infeasible.


┌─────────────────────────────────────────────────────────────────────────────┐
│ 2.4 COMMAND REFERENCE                                                       │
└─────────────────────────────────────────────────────────────────────────────┘

┌────────────────┬────────────────────────────────────────────────────────────┐
│ Command        │ Description                                                │
├────────────────┼────────────────────────────────────────────────────────────┤
│ commit, c      │ Create new commit with current changes                     │
│ status, s      │ Show working directory status (modified/added/deleted)    │
│ log, l         │ Show commit history (Merkle DAG traversal)                 │
│ diff, d        │ Compare two commits                                        │
│ revert, rv     │ Create new commit that undoes previous commit              │
│ branch, b      │ List or create branches                                    │
│ checkout, co   │ Switch branches (restores files from tree)                │
│ delete-branch  │ Delete a branch                                            │
│ verify, v      │ Verify repository integrity (rehash all objects)          │
│ dir, ls        │ List directory with Git status colors                      │
│ cd             │ Change directory (supports quoted paths)                   │
│ users          │ Manage users (admin only)                                  │
│ grant          │ Grant repository access (admin only)                       │
│ whoami         │ Show current user info                                     │
│ permissions    │ Show current user's permissions                            │
│ info           │ Show repository information                                │
└────────────────┴────────────────────────────────────────────────────────────┘


================================================================================
3. .GITCLONE FOLDER STRUCTURE
================================================================================

┌─────────────────────────────────────────────────────────────────────────────┐
│ 3.1 DIRECTORY LAYOUT                                                        │
└─────────────────────────────────────────────────────────────────────────────┘

    .gitclone/
    │
    ├── HEAD                      # Current branch pointer
    │
    ├── auth.json                 # User database (PBKDF2 hashed passwords)
    │
    ├── objects/                  # Content-addressable storage
    │   ├── ab/                   # First 2 chars of hash (00-FF)
    │   │   └── cd1234...         # Remaining 62 chars (actual object)
    │   ├── ef/
    │   │   └── gh5678...
    │   └── ...
    │
    └── refs/
        └── heads/                # Branch references
            ├── main              # Main branch (contains commit hash)
            ├── feature           # Feature branch
            └── ...


┌─────────────────────────────────────────────────────────────────────────────┐
│ 3.2 OBJECT STORAGE FORMAT                                                   │
└─────────────────────────────────────────────────────────────────────────────┘

BLOB OBJECT (JSON):

    {
        "Type": "blob",
        "Hash": "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3",
        "Data": "base64_encoded_file_content"
    }

TREE OBJECT (JSON):

    {
        "Type": "tree",
        "Hash": "d3b07384d113edec49eaa6238ad5ff00",
        "Entries": [
            {
                "Mode": "100644",      # File
                "Name": "README.md",
                "Hash": "blob_hash_here",
                "Type": "blob"
            },
            {
                "Mode": "040000",      # Directory
                "Name": "src",
                "Hash": "tree_hash_here",
                "Type": "tree"
            }
        ]
    }

COMMIT OBJECT (JSON):

    {
        "Type": "commit",
        "Hash": "e7cf3ef4f17c3999a94f2c6f612e8a888e5b1027",
        "TreeHash": "root_tree_hash",
        "ParentHash": "parent_commit_hash_or_null",
        "Author": "username",
        "Message": "commit message",
        "Timestamp": "2024-01-15T10:30:00Z"
    }


┌─────────────────────────────────────────────────────────────────────────────┐
│ 3.3 REFERENCE STORAGE                                                        │
└─────────────────────────────────────────────────────────────────────────────┘

HEAD file (.gitclone/HEAD):

    Contains: refs/heads/main
    
    This points to the current branch. Can also contain a commit hash
    for detached HEAD state.

BRANCH file (.gitclone/refs/heads/main):

    Contains: e7cf3ef4f17c3999a94f2c6f612e8a888e5b1027
    
    Just the 64-character commit hash of the branch tip.


┌─────────────────────────────────────────────────────────────────────────────┐
│ 3.4 AUTHENTICATION STORAGE                                                   │
└─────────────────────────────────────────────────────────────────────────────┘

auth.json format:

    [
        {
            "Username": "admin",
            "PasswordHash": "base64_encoded_pbkdf2_hash",
            "Salt": "base64_encoded_salt",
            "Email": "admin@localhost",
            "Role": 4,
            "CreatedAt": "2024-01-15T10:00:00Z",
            "LastLogin": "2024-01-15T12:00:00Z",
            "IsActive": true,
            "AllowedRepositories": ["*"]
        }
    ]

PASSWORD HASHING PROCESS:

    Plain Password: "mySecurePassword123"
           │
           ▼
    + Salt (random 32 bytes)
           │
           ▼
    310,000 iterations of PBKDF2-HMAC-SHA256
           │
           ▼
    Final Hash (32 bytes) + Salt stored separately


================================================================================
4. MATHEMATICAL EXAMPLES
================================================================================

┌─────────────────────────────────────────────────────────────────────────────┐
│ 4.1 HASH COMPUTATION EXAMPLE                                                │
└─────────────────────────────────────────────────────────────────────────────┘

Given a file with content "Hello World":

    Step 1: Convert to bytes
    UTF-8: 48 65 6c 6c 6f 20 57 6f 72 6c 64
    
    Step 2: Apply SHA-256
    
    Input bytes: 0x48656c6c6f20576f726c64
    
    SHA-256 computation (simplified):
    
    Initial hash values (first 32 bits of fractional parts of square roots):
    h0 = 0x6a09e667
    h1 = 0xbb67ae85
    h2 = 0x3c6ef372
    h3 = 0xa54ff53a
    ... (8 total)
    
    After 64 rounds of compression:
    
    Step 3: Output
    
    Final hash (hex): a94a8fe5ccb19ba61c4c0873d391e987982fbbd3
    
    Verification: 
    $ echo -n "Hello World" | sha256sum
    a94a8fe5ccb19ba61c4c0873d391e987982fbbd3


┌─────────────────────────────────────────────────────────────────────────────┐
│ 4.2 TREE STRUCTURE EXAMPLE                                                  │
└─────────────────────────────────────────────────────────────────────────────┘

Project structure:

    project/
    ├── README.md
    ├── src/
    │   ├── main.py
    │   └── utils.py
    └── tests/
        └── test_main.py

TREE CONSTRUCTION:

    Step 1: Hash leaf files (blobs)
    README.md:    hash_R = SHA-256(content_R)
    main.py:      hash_M = SHA-256(content_M)
    utils.py:     hash_U = SHA-256(content_U)
    test_main.py: hash_T = SHA-256(content_T)

    Step 2: Build src tree
    src_entries = [
        ("100644 main.py", hash_M),
        ("100644 utils.py", hash_U)
    ]
    src_hash = SHA-256(concatenate(src_entries))

    Step 3: Build tests tree
    tests_entries = [("100644 test_main.py", hash_T)]
    tests_hash = SHA-256(concatenate(tests_entries))

    Step 4: Build root tree
    root_entries = [
        ("100644 README.md", hash_R),
        ("040000 src", src_hash),
        ("040000 tests", tests_hash)
    ]
    root_hash = SHA-256(concatenate(root_entries))

RESULTING TREE:

    root_hash: "d3b07384d113edec49eaa6238ad5ff00"
         │
         ├── README.md ──► hash_R
         ├── src/ ────────► src_hash
         │                    ├── main.py ──► hash_M
         │                    └── utils.py ──► hash_U
         └── tests/ ──────► tests_hash
                              └── test_main.py ──► hash_T


┌─────────────────────────────────────────────────────────────────────────────┐
│ 4.3 COMMIT CHAIN EXAMPLE                                                    │
└─────────────────────────────────────────────────────────────────────────────┘

Initial commit:

    commit_1_hash = SHA-256(
        tree_hash_1 +        # Hash of initial project state
        "" +                 # No parent (null)
        "author" +
        "timestamp" +
        "Initial commit"
    )

Second commit:

    commit_2_hash = SHA-256(
        tree_hash_2 +        # Hash of modified state
        commit_1_hash +      # Parent pointer
        "author" +
        "timestamp" +
        "Added feature"
    )

Resulting chain:

    commit_1_hash ◄─── commit_2_hash ◄─── commit_3_hash
         │                  │                  │
         ▼                  ▼                  ▼
    tree_hash_1        tree_hash_2        tree_hash_3

VERIFICATION:

    To verify commit_3 is valid:
        1. Compute hash of commit_3 content → matches stored commit_3_hash?
        2. Compute hash of tree_hash_3 → verify all blobs match
        3. Recursively verify commit_2 (parent)
        4. Continue until genesis commit

This creates an immutable, verifiable history!


================================================================================
5. SECURITY FEATURES
================================================================================

┌─────────────────────────────────────────────────────────────────────────────┐
│ 5.1 PASSWORD HASHING (PBKDF2)                                               │
└─────────────────────────────────────────────────────────────────────────────┘

WHY NOT JUST SHA-256?

    SHA-256 is FAST (billions of hashes per second with GPUs)
    
    PBKDF2 is SLOW (only thousands of hashes per second)
    
    This makes brute-force attacks 1,000,000x harder!

PBKDF2 CONFIGURATION:

    ┌─────────────────────────────────────────────────────────────┐
    │ Algorithm:    PBKDF2-HMAC-SHA256                            │
    │ Iterations:   310,000 (OWASP 2024 recommendation)          │
    │ Salt length:  32 bytes (256 bits)                          │
    │ Output size:  32 bytes (256 bits)                          │
    │ Purpose:      Password storage (never store plain text)    │
    └─────────────────────────────────────────────────────────────┘

VERIFICATION PROCESS:

    When user logs in:
    
    1. Retrieve stored Salt for username
    2. Compute PBKDF2(entered_password + Salt, 310k iterations)
    3. Compare with stored PasswordHash
    4. If equal → authentication successful
    5. If not equal → authentication failed

TIMING ATTACK PROTECTION:

    CryptographicOperations.FixedTimeEquals()
    
    Compares hashes in constant time, preventing timing attacks
    that could leak information about the password.


┌─────────────────────────────────────────────────────────────────────────────┐
│ 5.2 SESSION MANAGEMENT                                                      │
└─────────────────────────────────────────────────────────────────────────────┘

TOKEN GENERATION:

    Session token = Base64(RandomNumberGenerator.GetBytes(64))
    
    Length: 64 bytes = 512 bits of entropy
    
    This is cryptographically secure random data.

SESSION STORAGE:

    Session {
        Token: "random_64_bytes_base64",
        Username: "user123",
        CreatedAt: DateTime.UtcNow,
        ExpiresAt: DateTime.UtcNow.AddHours(8),
        LastActivity: DateTime.UtcNow
    }

VALIDATION:

    Each request:
    1. Extract token from active session
    2. Check token exists in session store
    3. Check ExpiresAt > current time
    4. Update LastActivity
    5. Proceed if valid, reject if expired/invalid


┌─────────────────────────────────────────────────────────────────────────────┐
│ 5.3 ROLE-BASED ACCESS CONTROL                                               │
└─────────────────────────────────────────────────────────────────────────────┘

PERMISSION CHECK ALGORITHM:

    bool HasPermission(User user, Permission required) {
        UserPermissions = GetPermissionsForRole(user.Role);
        return (UserPermissions & required) == required;
    }

EXAMPLE CHECK:

    User has role "Developer" → Permissions = 47 (Read|Write|Commit|CreateBranch|Merge)
    
    Check if user can commit:
        Required = 4 (Commit)
        47 & 4 = 4 → True (has permission)
    
    Check if user can delete branch:
        Required = 16 (DeleteBranch)
        47 & 16 = 0 → False (no permission)

REPOSITORY ACCESS CONTROL:

    Users can be restricted to specific repositories:
    
    "AllowedRepositories": ["repo1", "repo2"]  → Only these repos
    "AllowedRepositories": ["*"]               → All repositories
    "AllowedRepositories": []                  → No repositories


================================================================================
6. PERFORMANCE CHARACTERISTICS
================================================================================

┌─────────────────────────────────────────────────────────────────────────────┐
│ TIME COMPLEXITY                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────┬────────────────────────────────────────────────┐
│ Operation                  │ Complexity                                     │
├────────────────────────────┼────────────────────────────────────────────────┤
│ Hash computation           │ O(n) where n = file size                      │
│ Tree comparison            │ O(log n) for changed files                    │
│ Commit creation            │ O(n) for all files in working directory       │
│ Checkout (branch switch)   │ O(m) where m = total files in commit          │
│ Find object by hash        │ O(1) - direct file access                     │
│ Status check               │ O(f) where f = number of files                │
│ Revert commit              │ O(d) where d = files changed in commit        │
└────────────────────────────┴────────────────────────────────────────────────┘


┌─────────────────────────────────────────────────────────────────────────────┐
│ SPACE COMPLEXITY                                                            │
└─────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────┬────────────────────────────────────────────────┐
│ Component                  │ Space usage                                    │
├────────────────────────────┼────────────────────────────────────────────────┤
│ Per file (blob)            │ File size + ~200 bytes metadata               │
│ Per directory (tree)       │ ~100 bytes + 100 bytes per entry              │
│ Per commit                 │ ~300 bytes + message length                   │
│ Deduplication              │ Same content stored once (by hash)            │
│ Object sharding            │ 256 directories max 65k files each            │
└────────────────────────────┴────────────────────────────────────────────────┘


================================================================================
7. TROUBLESHOOTING
================================================================================

COMMON ISSUES AND SOLUTIONS:

┌─────────────────────────────────────────────────────────────────────────────┐
│ ISSUE: "User does not have access to repository"                           │
│ SOLUTION: Admin must run: grant <username> <repository-name>               │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ ISSUE: "Commit not found" when using short hash                            │
│ SOLUTION: Use more characters of the hash or full 64-character hash        │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ ISSUE: "Insufficient permissions"                                           │
│ SOLUTION: Check your role with 'permissions' command.                      │
│           Admin can upgrade with: users add <user> <pass> <email> admin    │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ ISSUE: Checkout doesn't restore files                                       │
│ SOLUTION: Ensure you're not outside the repository. Use 'cd' to go back.   │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│ ISSUE: HEAD file missing                                                    │
│ SOLUTION: Repository will auto-create HEAD on next initialization.         │
│           Or manually create: echo "refs/heads/main" > .gitclone/HEAD      │
└─────────────────────────────────────────────────────────────────────────────┘


================================================================================
8. CONCLUSION
================================================================================

This Git clone implementation demonstrates the core mathematical principles
that make distributed version control possible:

1. CRYPTOGRAPHIC HASHING provides content addressing and integrity verification
2. MERKLE TREES enable efficient storage and comparison of directory structures
3. MERKLE DAG creates immutable, verifiable history with branches
4. CONTENT-ADDRESSABLE STORAGE eliminates duplicates and enables fast lookups

These same principles power Git, blockchain, IPFS, and many other distributed
systems. The implementation is a working example of how mathematics enables
modern version control.
